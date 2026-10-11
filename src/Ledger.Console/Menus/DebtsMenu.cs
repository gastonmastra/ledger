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
        ("Record a payment (pays the oldest debts first)", RecordPaymentAsync),
        ("Payments of a debt", DebtPaymentsAsync),
        ("Paid debts (history)", PaidDebtsAsync),
        ("Loans, splits and payments (edit, delete, restore, history)", ct => operations.RunAsync(null, ct))
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
        DebtsTable(debts.Debts);

        ConsoleUi.Pause();
    }

    private static void DebtsTable(IEnumerable<Debt> debts)
        => ConsoleUi.Table(debts,
            ("With", d => d.GroupName is null ? d.CounterpartyName : $"{d.CounterpartyName} ({d.GroupName})"),
            ("Direction", d => d.Direction == "OwedToMe" ? "owes me" : "I owe"),
            ("Left", d => ConsoleUi.Money(d.Remaining, d.Currency)),
            ("Of", d => ConsoleUi.Money(d.Amount, d.Currency)),
            ("For", d => d.Description),
            ("Status", d => (d.IsSettling ? "Settling" : d.IsPartiallyPaid ? "Partially paid" : d.Status)
                            + (d.PendingChange ? " · change pending" : "")
                            + (d.Disputed ? " · disputed" : "")));

    private async Task PaidDebtsAsync(CancellationToken ct)
    {
        var paid = (await api.DebtsAsync(includeSettled: true, ct)).Debts.Where(d => d.Status == "Settled").ToList();
        DebtsTable(paid);
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

    private async Task RecordPaymentAsync(CancellationToken ct)
    {
        var contacts = await api.ContactsAsync(ct);
        var balances = (await api.DebtsAsync(false, ct)).ByContact;

        if (ConsoleUi.Choose("With", contacts, c => c.Name + string.Concat(balances
                .Where(b => b.PersonId == c.PersonId)
                .Select(b => $" · net {ConsoleUi.Money(b.Net, b.Currency)}"))) is not { } contact)
        {
            ConsoleUi.Pause();
            return;
        }

        var mine = balances.Where(b => b.PersonId == contact.PersonId).ToList();
        var currency = mine.Count == 1 ? mine[0].Currency : ConsoleUi.Choose("Currency", ["EUR", "USD", "ARS"], c => c)!;
        var net = mine.FirstOrDefault(b => b.Currency == currency)?.Net ?? 0;

        var theyPaid = ConsoleUi.Choose("Who paid",
            [$"{contact.Name} paid me", $"I paid {contact.Name}"], d => d) == $"{contact.Name} paid me";
        var direction = theyPaid ? "TheyPaidMe" : "IPaidThem";
        var suggested = (theyPaid && net > 0) || (!theyPaid && net < 0) ? Math.Abs(net) : 0;
        var amount = suggested > 0
            ? ConsoleUi.ReadDecimalOrKeep($"Amount ({currency})", suggested)
            : ConsoleUi.ReadDecimal($"Amount ({currency}): ");

        var preview = await api.PreviewPaymentAsync(new { contactId = contact.Id, currency, direction, amount }, ct);
        ShowPreview(preview, currency, contact.Name);

        if (!ConsoleUi.Confirm("Record it?"))
        {
            ConsoleUi.Pause();
            return;
        }

        if (await ChooseWalletAsync(currency, theyPaid, ct) is not { } wallet)
            return;

        var description = ConsoleUi.ReadText("Description (optional): ", optional: true);
        var when = ConsoleUi.ReadWhen();
        var operation = await api.RecordPaymentAsync(
            contact.Id, currency, direction, amount, wallet.Id, description.Length == 0 ? null : description, when, ct);

        ShowOperation(await api.WaitForOperationAsync(operation, ct), "Payment recorded.");
    }

    /// <summary>Which debts a payment pays and what is left, from my point of view.</summary>
    internal static void ShowPreview(PaymentPreview preview, string currency, string name)
    {
        SystemConsole.WriteLine("\nIt pays (oldest first):");
        ConsoleUi.Table(preview.Lines,
            ("Debt", l => $"{(l.Direction == "OwedToMe" ? $"{name} owed me" : $"I owed {name}")} · {l.Description}"),
            ("Date", l => l.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd")),
            ("Paid", l => ConsoleUi.Money(l.Paid, currency)),
            ("Left", l => l.RemainingAfter == 0 ? "paid" : $"{ConsoleUi.Money(l.RemainingAfter, currency)} of {ConsoleUi.Money(l.DebtAmount, currency)}"));

        if (preview.Extra > 0)
            SystemConsole.WriteLine(preview.ExtraDirection == "OwedToMe"
                ? $"Paid beyond what was owed: {name} now owes you {ConsoleUi.Money(preview.Extra, currency)}."
                : $"Paid beyond what was owed: you now owe {name} {ConsoleUi.Money(preview.Extra, currency)}.");

        SystemConsole.WriteLine($"Balance afterwards: {ConsoleUi.Money(preview.BalanceAfter, currency)} (positive: they owe you).\n");
    }

    private async Task DebtPaymentsAsync(CancellationToken ct)
    {
        var debts = (await api.DebtsAsync(includeSettled: true, ct)).Debts.Where(d => d.GroupId is null && d.Paid > 0).ToList();

        if (ConsoleUi.Choose("Debt", debts,
                d => $"{(d.Direction == "OwedToMe" ? $"{d.CounterpartyName} owes me" : $"I owe {d.CounterpartyName}")} · {d.Description} · {ConsoleUi.Money(d.Paid, d.Currency)} paid of {ConsoleUi.Money(d.Amount, d.Currency)}")
            is not { } debt)
        {
            ConsoleUi.Pause();
            return;
        }

        ConsoleUi.Table(await api.DebtPaymentsAsync(debt.Id, ct),
            ("Date", p => p.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")),
            ("Payment", p => p.Description),
            ("Paid", p => ConsoleUi.Money(p.Amount, debt.Currency)),
            ("Status", p => p.Status == "Pending" ? "being recorded" : ""));
        SystemConsole.WriteLine($"Left: {ConsoleUi.Money(debt.Remaining, debt.Currency)}");
        ConsoleUi.Pause();
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
