using Ledger.Console.Api;

namespace Ledger.Console.Menus;

/// <summary>Money that other users recorded on my behalf and that still needs a wallet.</summary>
public sealed class PendingMenu(LedgerApiClient api) : Menu
{
    protected override string Title => "PENDING WALLET ASSIGNMENTS";

    protected override IReadOnlyList<(string, Func<CancellationToken, Task>)> Actions =>
    [
        ("List", ListAsync),
        ("Choose the wallet for an item", AssignAsync)
    ];

    private async Task ListAsync(CancellationToken ct)
    {
        ConsoleUi.Table(await api.PendingAssignmentsAsync(ct),
            ("When", p => p.OccurredAt),
            ("What", p => p.Description),
            ("With", p => p.CounterpartyName),
            ("Money", p => p.Amount > 0 ? $"received {ConsoleUi.Money(p.Amount, p.Currency)}" : $"paid {ConsoleUi.Money(-p.Amount, p.Currency)}"));
        ConsoleUi.Pause();
    }

    private async Task AssignAsync(CancellationToken ct)
    {
        var pending = await api.PendingAssignmentsAsync(ct);

        if (ConsoleUi.Choose("Item", pending,
                p => $"{p.Description} — {(p.Amount > 0 ? "received" : "paid")} {ConsoleUi.Money(Math.Abs(p.Amount), p.Currency)}") is not { } item)
        {
            ConsoleUi.Pause();
            return;
        }

        var wallets = (await api.WalletsAsync(false, ct)).Where(w => w.Currency == item.Currency).ToList();

        if (ConsoleUi.Choose(item.Amount > 0 ? "Received in" : "Paid from", wallets, WalletsMenu.Label) is not { } wallet)
        {
            ConsoleUi.Pause();
            return;
        }

        var when = ConsoleUi.ReadWhen("When (yyyy-MM-dd HH:mm, ENTER = date of the operation): ", item.OccurredAt);

        await api.AssignWalletAsync(item.Id, wallet.Id, when, ct);
        ConsoleUi.Success($"Recorded in {wallet.Name}.");
    }
}
