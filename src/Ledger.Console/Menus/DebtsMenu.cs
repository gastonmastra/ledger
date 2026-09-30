using Ledger.Console.Api;
using SystemConsole = System.Console;

namespace Ledger.Console.Menus;

public sealed class DebtsMenu(LedgerApiClient api, SplitWizard splitWizard, OperationsScreen operations) : Menu
{
    protected override string Title => "DEBTS & SPLITS";

    protected override IReadOnlyList<(string, Func<CancellationToken, Task>)> Actions =>
    [
        ("Who owes what", OverviewAsync),
        ("Lend money to a contact", LendAsync),
        ("Split an expense with contacts", SplitAsync),
        ("A friend paid for me", FriendPaidAsync),
        ("Add an existing debt", InitialDebtAsync),
        ("Settle one debt", SettleDebtAsync),
        ("Settle up with a contact", SettleContactAsync),
        ("Loans, splits and settlements (edit, delete, restore, history)", ct => operations.RunAsync(null, ct))
    ];

    private async Task OverviewAsync(CancellationToken ct)
    {
        var debts = await api.DebtsAsync(includeSettled: false, ct);

        SystemConsole.WriteLine("Per contact (outside groups):");
        ConsoleUi.Table(debts.ByContact,
            ("Contact", c => c.Name),
            ("Owes me", c => ConsoleUi.Money(c.OwedToMe, c.Currency)),
            ("I owe", c => ConsoleUi.Money(c.IOwe, c.Currency)),
            ("Net", c => ConsoleUi.Money(c.Net, c.Currency)));

        SystemConsole.WriteLine("\nOpen debts:");
        ConsoleUi.Table(debts.Debts,
            ("With", d => d.GroupName is null ? d.CounterpartyName : $"{d.CounterpartyName} ({d.GroupName})"),
            ("Direction", d => d.Direction == "OwedToMe" ? "owes me" : "I owe"),
            ("Amount", d => ConsoleUi.Money(d.Amount, d.Currency)),
            ("For", d => d.Description),
            ("Status", d => (d.IsSettling ? "Settling" : d.Status)
                            + (d.PendingChange ? " · change pending" : "")
                            + (d.Disputed ? " · disputed" : "")));

        ConsoleUi.Pause();
    }

    private async Task LendAsync(CancellationToken ct)
    {
        if (ConsoleUi.Choose("To", await api.ContactsAsync(ct), c => c.Name) is not { } contact
            || ConsoleUi.Choose("From wallet", await api.WalletsAsync(false, ct), WalletsMenu.Label) is not { } wallet)
        {
            ConsoleUi.Pause();
            return;
        }

        var amount = ConsoleUi.ReadDecimal($"Amount ({wallet.Currency}): ");
        var description = ConsoleUi.ReadText("Description (optional): ", optional: true);
        var when = ConsoleUi.ReadWhen();

        var operation = await api.LendAsync(contact.Id, wallet.Id, amount, description, when, ct);

        ShowOperation(await api.WaitForOperationAsync(operation, ct), $"{contact.Name} owes you {ConsoleUi.Money(amount, wallet.Currency)}.");
    }

    private async Task SplitAsync(CancellationToken ct)
    {
        var me = await api.MeAsync(ct);
        var people = (await api.ContactsAsync(ct))
            .Select(c => new Person(c.PersonId, c.Name, false))
            .Prepend(new Person(me.PersonId, me.DisplayName, true))
            .ToList();

        if (await splitWizard.RunAsync(null, people, ct) is { } operation)
            ShowOperation(operation, "Expense split.");
        else
            ConsoleUi.Pause();
    }

    private async Task FriendPaidAsync(CancellationToken ct)
    {
        var me = await api.MeAsync(ct);

        if (ConsoleUi.Choose("Who paid for you", await api.ContactsAsync(ct), c => c.Name) is not { } friend)
        {
            ConsoleUi.Pause();
            return;
        }

        var amount = ConsoleUi.ReadDecimal("Amount: ");
        var currency = ConsoleUi.Choose("Currency", ["EUR", "USD", "ARS"], c => c)!;
        var description = ConsoleUi.ReadText("What was it? ");
        var categories = (await api.CategoriesAsync(ct)).Where(c => c.Type == "Expense").ToList();
        var category = categories.Count == 0 ? null : ConsoleUi.Choose("Category (0 = uncategorized)", categories, c => c.Name, allowNone: true);

        var when = ConsoleUi.ReadWhen();

        var operation = await api.SplitAsync(
            null, amount, currency, "Custom",
            [new SplitPayer(friend.PersonId, amount)],
            [new SplitParticipant(me.PersonId, amount)],
            category?.Id, description, when, ct);

        ShowOperation(await api.WaitForOperationAsync(operation, ct), $"You owe {friend.Name} {ConsoleUi.Money(amount, currency)}.");
    }

    private async Task InitialDebtAsync(CancellationToken ct)
    {
        var direction = ConsoleUi.Choose("What was it", ["I owed them", "They owed me"], d => d)!;
        var iOwe = direction == "I owed them";

        if (ConsoleUi.Choose("Contact", await api.ContactsAsync(ct), c => c.Name) is not { } contact)
        {
            ConsoleUi.Pause();
            return;
        }

        var amount = ConsoleUi.ReadDecimal("Amount: ");
        var currency = ConsoleUi.Choose("Currency", ["EUR", "USD", "ARS"], c => c)!;
        var description = ConsoleUi.ReadText("Description (optional): ", optional: true);
        var when = ConsoleUi.ReadWhen("When did it start? (yyyy-MM-dd HH:mm, ENTER = now): ");

        var operation = await api.RecordInitialDebtAsync(
            contact.Id, iOwe ? "IOwe" : "TheyOweMe", amount, currency,
            description.Length == 0 ? null : description, when, ct);

        ShowOperation(await api.WaitForOperationAsync(operation, ct),
            iOwe ? $"Recorded: you owe {contact.Name} {ConsoleUi.Money(amount, currency)}."
                 : $"Recorded: {contact.Name} owes you {ConsoleUi.Money(amount, currency)}.");
    }

    private async Task SettleDebtAsync(CancellationToken ct)
    {
        var debts = (await api.DebtsAsync(false, ct)).Debts
            .Where(d => d.GroupId is null && d.Status == "Open" && !d.IsSettling && !d.PendingChange)
            .ToList();

        if (ConsoleUi.Choose("Debt", debts,
                d => $"{(d.Direction == "OwedToMe" ? $"{d.CounterpartyName} owes me" : $"I owe {d.CounterpartyName}")} {ConsoleUi.Money(d.Amount, d.Currency)} ({d.Description})")
            is not { } debt)
        {
            ConsoleUi.Pause();
            return;
        }

        if (await ChooseWalletAsync(debt.Currency, debt.Direction == "OwedToMe", ct) is not { } wallet)
            return;

        var when = ConsoleUi.ReadWhen();
        var operation = await api.SettleAsync(new { scope = "Debt", debtId = debt.Id, walletId = wallet.Id, occurredAt = when }, ct);
        ShowOperation(await api.WaitForOperationAsync(operation, ct), "Debt settled.");
    }

    private async Task SettleContactAsync(CancellationToken ct)
    {
        var balances = (await api.DebtsAsync(false, ct)).ByContact;

        if (ConsoleUi.Choose("Contact", balances,
                b => $"{b.Name}: net {ConsoleUi.Money(b.Net, b.Currency)} ({(b.Net >= 0 ? "they pay you" : "you pay them")})")
            is not { } balance)
        {
            ConsoleUi.Pause();
            return;
        }

        var contact = (await api.ContactsAsync(ct)).Single(c => c.PersonId == balance.PersonId);
        Guid? walletId = null;

        if (balance.Net != 0)
        {
            if (await ChooseWalletAsync(balance.Currency, balance.Net > 0, ct) is not { } wallet)
                return;

            walletId = wallet.Id;
        }

        var when = ConsoleUi.ReadWhen();
        var operation = await api.SettleAsync(
            new { scope = "Contact", contactId = contact.Id, currency = balance.Currency, walletId, occurredAt = when }, ct);

        ShowOperation(await api.WaitForOperationAsync(operation, ct), $"All settled with {contact.Name}.");
    }

    private async Task<Wallet?> ChooseWalletAsync(string currency, bool receiving, CancellationToken ct)
    {
        var wallets = (await api.WalletsAsync(false, ct)).Where(w => w.Currency == currency).ToList();
        var wallet = ConsoleUi.Choose(receiving ? "Received in" : "Paid from", wallets, WalletsMenu.Label);

        if (wallet is null)
            ConsoleUi.Pause();

        return wallet;
    }
}
