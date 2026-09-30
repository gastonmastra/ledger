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
        ("Transfer between my wallets", TransferAsync)
    ];

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
                SystemConsole.WriteLine(
                    $"{transaction.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}  {transaction.Kind,-15} {transaction.Description}");

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
        var filter = new TransactionFilter();

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

        await api.RecordIncomeAsync(wallet.Id, amount, category?.Id, description, ct);
        ConsoleUi.Success("Income recorded.");
    }

    private async Task ExpenseAsync(CancellationToken ct)
    {
        if (await ChooseWalletAsync(ct) is not { } wallet)
            return;

        var amount = ConsoleUi.ReadDecimal($"Amount ({wallet.Currency}): ");
        var category = await ChooseCategoryAsync("Expense", ct);
        var description = ConsoleUi.ReadText("Description (optional): ", optional: true);

        await api.RecordExpenseAsync(wallet.Id, amount, category?.Id, description, ct);
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

        await api.TransferAsync(from.Id, to.Id, amount, received, description, ct);
        ConsoleUi.Success("Transfer recorded.");
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
