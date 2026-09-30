using Ledger.Console.Api;
using SystemConsole = System.Console;

namespace Ledger.Console.Menus;

public sealed class TransactionsMenu(LedgerApiClient api) : Menu
{
    protected override string Title => "TRANSACTIONS";

    protected override IReadOnlyList<(string, Func<CancellationToken, Task>)> Actions =>
    [
        ("List transactions (newest first)", ListAsync),
        ("Record income", IncomeAsync),
        ("Record expense", ExpenseAsync),
        ("Transfer between my wallets", TransferAsync),
        ("Edit a transaction", EditAsync),
        ("Delete a transaction", DeleteAsync),
        ("Restore a deleted transaction", RestoreAsync),
        ("History of a transaction", HistoryAsync)
    ];

    private static readonly string[] Restorable = ["Income", "Expense", "Transfer", "BalanceAdjustment"];

    private async Task ListAsync(CancellationToken ct)
    {
        var filter = new TransactionFilter();

        if (ConsoleUi.Confirm("Filter the list?"))
            filter = await ReadFilterAsync(ct);

        while (true)
        {
            var page = await api.TransactionsAsync(filter, ct);

            ConsoleUi.Header($"TRANSACTIONS · page {page.Page}/{Math.Max(1, page.TotalPages)} ({page.TotalCount})");

            foreach (var transaction in page.Items)
            {
                var status = transaction.Status == "Current"
                    ? transaction.Revision > 1 ? " (edited)" : ""
                    : $" [{transaction.Status}]";

                SystemConsole.WriteLine(
                    $"{transaction.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}  {transaction.Kind,-15} {transaction.Description}{status}");

                if (transaction.Status == "Reversal" && transaction.ChangeReason is { } reason)
                    SystemConsole.WriteLine($"      why: {reason}");

                foreach (var posting in transaction.Postings)
                {
                    var counterparty = posting.CounterpartyName is null ? "" : $" [{posting.CounterpartyName}]";
                    SystemConsole.WriteLine(
                        $"      {ConsoleUi.Money(posting.Amount, posting.Currency),18}  {posting.AccountName}{counterparty}");
                }
            }

            if (page.Items.Count == 0)
                SystemConsole.WriteLine("(no transactions)");

            SystemConsole.Write("\nn = next page, p = previous page, ENTER = back: ");
            var key = ConsoleUi.ReadLine().Trim().ToLowerInvariant();

            if (key == "n" && page.Page < page.TotalPages)
                filter = filter with { Page = filter.Page + 1 };
            else if (key == "p" && page.Page > 1)
                filter = filter with { Page = filter.Page - 1 };
            else if (key is not ("n" or "p"))
                return;
        }
    }

    private async Task<TransactionFilter> ReadFilterAsync(CancellationToken ct)
    {
        var filter = new TransactionFilter(IncludeHistory: ConsoleUi.Confirm("Also show changed and deleted versions?"));

        if (ConsoleUi.Confirm("By wallet?"))
            filter = filter with { WalletId = ConsoleUi.Choose("Wallet", await api.WalletsAsync(true, ct), w => w.Name)?.Id };

        if (ConsoleUi.Confirm("By category?"))
            filter = filter with { CategoryId = ConsoleUi.Choose("Category", await api.CategoriesAsync(ct), c => c.Name)?.Id };

        if (ConsoleUi.Confirm("By contact?"))
            filter = filter with { ContactId = ConsoleUi.Choose("Contact", await api.ContactsAsync(ct), c => c.Name)?.Id };

        if (ConsoleUi.Confirm("By date range?"))
        {
            filter = filter with
            {
                From = ConsoleUi.ReadOptionalDate("From (yyyy-MM-dd, ENTER to skip): "),
                To = ConsoleUi.ReadOptionalDate("To (yyyy-MM-dd, ENTER to skip): ")?.AddDays(1).AddTicks(-1)
            };
        }

        return filter;
    }

    private async Task IncomeAsync(CancellationToken ct)
    {
        if (await ChooseWalletAsync(ct) is not { } wallet)
            return;

        var amount = ConsoleUi.ReadDecimal($"Amount ({wallet.Currency}): ");
        var category = await ChooseCategoryAsync("Income", ct);
        var description = ConsoleUi.ReadText("Description (optional): ", optional: true);
        var when = ConsoleUi.ReadWhen();

        await api.RecordIncomeAsync(wallet.Id, amount, category?.Id, description, when, ct);
        ConsoleUi.Success("Income recorded.");
    }

    private async Task ExpenseAsync(CancellationToken ct)
    {
        if (await ChooseWalletAsync(ct) is not { } wallet)
            return;

        var amount = ConsoleUi.ReadDecimal($"Amount ({wallet.Currency}): ");
        var category = await ChooseCategoryAsync("Expense", ct);
        var description = ConsoleUi.ReadText("Description (optional): ", optional: true);
        var when = ConsoleUi.ReadWhen();

        await api.RecordExpenseAsync(wallet.Id, amount, category?.Id, description, when, ct);
        ConsoleUi.Success("Expense recorded.");
    }

    private async Task TransferAsync(CancellationToken ct)
    {
        var wallets = await api.WalletsAsync(false, ct);

        if (ConsoleUi.Choose("From wallet", wallets, WalletsMenu.Label) is not { } from
            || ConsoleUi.Choose("To wallet", wallets.Where(w => w.Id != from.Id).ToList(), WalletsMenu.Label) is not { } to)
        {
            ConsoleUi.Pause();
            return;
        }

        var amount = ConsoleUi.ReadDecimal($"Amount to send ({from.Currency}): ");
        decimal? received = from.Currency == to.Currency
            ? null
            : ConsoleUi.ReadDecimal($"Amount received ({to.Currency}): ");
        var description = ConsoleUi.ReadText("Description (optional): ", optional: true);
        var when = ConsoleUi.ReadWhen();

        await api.TransferAsync(from.Id, to.Id, amount, received, description, when, ct);
        ConsoleUi.Success("Transfer recorded.");
    }

    private async Task EditAsync(CancellationToken ct)
    {
        if (await ChooseTransactionAsync("Transaction to edit", t => t.Editable, false, ct) is not { } transaction)
            return;

        var wallets = await api.WalletsAsync(false, ct);
        var description = ConsoleUi.ReadTextOrKeep("Description", transaction.Description);

        if (transaction.Kind == "Transfer")
        {
            var fromPosting = transaction.Postings.First(p => p.WalletId is not null && p.Amount < 0);
            var toPosting = transaction.Postings.First(p => p.WalletId is not null && p.Amount > 0);
            var from = wallets.FirstOrDefault(w => w.Id == fromPosting.WalletId);
            var to = wallets.FirstOrDefault(w => w.Id == toPosting.WalletId);

            if (from is null || to is null || ConsoleUi.Confirm("Change the wallets?"))
            {
                from = ConsoleUi.Choose("From wallet", wallets, WalletsMenu.Label);
                to = from is null ? null : ConsoleUi.Choose("To wallet", wallets.Where(w => w.Id != from.Id).ToList(), WalletsMenu.Label);

                if (from is null || to is null)
                {
                    ConsoleUi.Pause();
                    return;
                }
            }

            var amount = ConsoleUi.ReadDecimalOrKeep($"Amount sent ({from.Currency})", -fromPosting.Amount);
            decimal? received = from.Currency == to.Currency
                ? null
                : ConsoleUi.ReadDecimalOrKeep($"Amount received ({to.Currency})", toPosting.Amount);
            var when = ConsoleUi.ReadWhen($"When [{transaction.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}] (ENTER = keep): ", transaction.OccurredAt);
            var reason = ConsoleUi.ReadReason();

            await api.ReviseTransactionAsync(transaction.Id, from.Id, to.Id, amount, received, null, description, when, reason, ct);
        }
        else
        {
            var walletPosting = transaction.Postings.First(p => p.WalletId is not null);
            var wallet = wallets.FirstOrDefault(w => w.Id == walletPosting.WalletId);

            if (wallet is null || ConsoleUi.Confirm("Change the wallet?"))
            {
                wallet = ConsoleUi.Choose("Wallet", wallets, WalletsMenu.Label);

                if (wallet is null)
                {
                    ConsoleUi.Pause();
                    return;
                }
            }

            var amount = ConsoleUi.ReadDecimalOrKeep($"Amount ({wallet.Currency})", Math.Abs(walletPosting.Amount));
            var categoryId = transaction.Postings.FirstOrDefault(p => p.CategoryId is not null)?.CategoryId;

            if (ConsoleUi.Confirm("Change the category?"))
                categoryId = (await ChooseCategoryAsync(transaction.Kind, ct))?.Id;

            var when = ConsoleUi.ReadWhen($"When [{transaction.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}] (ENTER = keep): ", transaction.OccurredAt);
            var reason = ConsoleUi.ReadReason();

            await api.ReviseTransactionAsync(transaction.Id, wallet.Id, null, amount, null, categoryId, description, when, reason, ct);
        }

        ConsoleUi.Success("Corrected. The previous version is kept in the history.");
    }

    private async Task DeleteAsync(CancellationToken ct)
    {
        if (await ChooseTransactionAsync("Transaction to delete", t => t.Deletable, false, ct) is not { } transaction)
            return;

        var reason = ConsoleUi.ReadReason();
        await api.DeleteTransactionAsync(transaction.Id, reason, ct);
        ConsoleUi.Success("Deleted: a reversal cancels it. It stays in the history and can be restored.");
    }

    private async Task RestoreAsync(CancellationToken ct)
    {
        if (await ChooseTransactionAsync(
                "Deleted transaction to restore", t => t.Status == "Deleted" && Restorable.Contains(t.Kind), true, ct)
            is not { } transaction)
            return;

        var reason = ConsoleUi.ReadReason();
        await api.RestoreTransactionAsync(transaction.Id, reason, ct);
        ConsoleUi.Success("Restored.");
    }

    private async Task HistoryAsync(CancellationToken ct)
    {
        if (await ChooseTransactionAsync("Transaction", _ => true, ConsoleUi.Confirm("Include changed and deleted ones?"), ct)
            is not { } transaction)
            return;

        foreach (var item in await api.TransactionHistoryAsync(transaction.Id, ct))
        {
            var indent = item.Status == "Reversal" ? "    " : "";
            SystemConsole.WriteLine(
                $"{indent}v{item.Revision} {item.Kind,-15} {item.Status,-9} {item.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}  {item.Description}");

            if (item.Action is not null)
            {
                var by = item.ChangedByMe || item.ChangedByName is null ? "" : $" by {item.ChangedByName}";
                SystemConsole.WriteLine($"{indent}   {item.Action}{by} — \"{item.Reason}\" (recorded {item.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm})");
            }

            foreach (var posting in item.Postings)
                SystemConsole.WriteLine($"{indent}   {ConsoleUi.Money(posting.Amount, posting.Currency),18}  {posting.AccountName}");
        }

        ConsoleUi.Pause();
    }

    /// <summary>Lets the user pick one of the latest 50 transactions that match.</summary>
    private async Task<Transaction?> ChooseTransactionAsync(
        string title, Func<Transaction, bool> filter, bool includeHistory, CancellationToken ct)
    {
        var candidates = new List<Transaction>();

        for (var page = 1; page <= 5 && candidates.Count < 50; page++)
        {
            var result = await api.TransactionsAsync(new TransactionFilter(page, IncludeHistory: includeHistory), ct);
            candidates.AddRange(result.Items.Where(filter));

            if (page >= result.TotalPages)
                break;
        }

        var transaction = ConsoleUi.Choose(title, candidates, t =>
        {
            var money = t.Postings.FirstOrDefault(p => p.WalletId is not null) ?? t.Postings[0];
            return $"{t.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm} {t.Kind} {ConsoleUi.Money(money.Amount, money.Currency)} {t.Description}";
        });

        if (transaction is null)
            ConsoleUi.Pause();

        return transaction;
    }

    private async Task<Wallet?> ChooseWalletAsync(CancellationToken ct)
    {
        var wallet = ConsoleUi.Choose("Wallet", await api.WalletsAsync(false, ct), WalletsMenu.Label);

        if (wallet is null)
            ConsoleUi.Pause();

        return wallet;
    }

    private async Task<Category?> ChooseCategoryAsync(string type, CancellationToken ct)
    {
        var categories = (await api.CategoriesAsync(ct)).Where(c => c.Type == type).ToList();

        return categories.Count == 0
            ? null
            : ConsoleUi.Choose($"{type} category (0 = uncategorized)", categories, c => c.Name, allowNone: true);
    }
}
