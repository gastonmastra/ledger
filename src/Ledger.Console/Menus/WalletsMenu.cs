using Ledger.Console.Api;

namespace Ledger.Console.Menus;

public sealed class WalletsMenu(LedgerApiClient api) : Menu
{
    private static readonly string[] Currencies = ["EUR", "USD", "ARS"];

    protected override string Title => "WALLETS";

    protected override IReadOnlyList<(string, Func<CancellationToken, Task>)> Actions =>
    [
        ("List wallets", ListAsync),
        ("Create wallet", CreateAsync),
        ("Rename wallet", RenameAsync),
        ("Change opening balance", OpeningBalanceAsync),
        ("Adjust balance to the real amount", AdjustAsync),
        ("Archive wallet", ArchiveAsync),
        ("Delete wallet", DeleteAsync)
    ];

    private async Task ListAsync(CancellationToken ct)
    {
        var wallets = await api.WalletsAsync(includeArchived: true, ct);

        ConsoleUi.Table(wallets,
            ("Name", w => w.Name),
            ("Balance", w => ConsoleUi.Money(w.Balance, w.Currency)),
            ("Status", w => w.IsReady ? w.Status : "Setting up..."));

        ConsoleUi.Pause();
    }

    private async Task CreateAsync(CancellationToken ct)
    {
        var name = ConsoleUi.ReadText("Name: ");
        var currency = ConsoleUi.Choose("Currency", Currencies, c => c)!;
        var opening = ConsoleUi.ReadOptionalDecimal("Opening balance (ENTER for 0): ") ?? 0;
        DateTimeOffset? openingAt = opening > 0
            ? ConsoleUi.ReadWhen("Date of the opening balance (yyyy-MM-dd HH:mm, ENTER = now): ")
            : null;

        var wallet = await api.CreateWalletAsync(name, currency, opening, openingAt, ct);

        ConsoleUi.Success($"Wallet '{wallet.Name}' created.");
    }

    private async Task RenameAsync(CancellationToken ct)
    {
        if (await PickAsync(ct) is not { } wallet)
            return;

        await api.RenameWalletAsync(wallet.Id, ConsoleUi.ReadText("New name: "), ct);
        ConsoleUi.Success("Wallet renamed.");
    }

    private async Task OpeningBalanceAsync(CancellationToken ct)
    {
        if (await PickAsync(ct) is not { } wallet)
            return;

        var current = await api.OpeningBalanceAsync(wallet.Id, ct);
        System.Console.WriteLine(
            $"Current opening balance: {ConsoleUi.Money(current.Amount, wallet.Currency)}" +
            (current.OccurredAt is { } at ? $" on {at.ToLocalTime():yyyy-MM-dd HH:mm}" : ""));

        var amount = ConsoleUi.ReadOptionalDecimal($"New amount (ENTER = {ConsoleUi.Amount(current.Amount)}): ") ?? current.Amount;
        var when = ConsoleUi.ReadWhen("Date (yyyy-MM-dd HH:mm, ENTER = keep): ", current.OccurredAt ?? wallet.CreatedAt);

        var reason = ConsoleUi.ReadReason();

        await api.ChangeOpeningBalanceAsync(wallet.Id, amount, when, reason, ct);
        ConsoleUi.Success("Opening balance updated. The previous one stays in the history as replaced.");
    }

    private async Task AdjustAsync(CancellationToken ct)
    {
        if (await PickAsync(ct) is not { } wallet)
            return;

        System.Console.WriteLine($"Balance in Ledger: {ConsoleUi.Money(wallet.Balance, wallet.Currency)}");
        var actual = ConsoleUi.ReadDecimal("Real balance: ");
        var description = ConsoleUi.ReadText("Description (optional): ", optional: true);
        var when = ConsoleUi.ReadWhen();

        await api.AdjustBalanceAsync(wallet.Id, actual, when, description.Length == 0 ? null : description, ct);
        ConsoleUi.Success("Balance adjusted.");
    }

    private async Task ArchiveAsync(CancellationToken ct)
    {
        if (await PickAsync(ct) is not { } wallet)
            return;

        await api.ArchiveWalletAsync(wallet.Id, ct);
        ConsoleUi.Success($"Wallet '{wallet.Name}' archived.");
    }

    private async Task DeleteAsync(CancellationToken ct)
    {
        if (await PickAsync(ct) is not { } wallet || !ConsoleUi.Confirm($"Delete '{wallet.Name}'?"))
            return;

        await api.DeleteWalletAsync(wallet.Id, ct);
        ConsoleUi.Success($"Wallet '{wallet.Name}' deleted.");
    }

    private async Task<Wallet?> PickAsync(CancellationToken ct)
    {
        var wallet = ConsoleUi.Choose("Wallet", await api.WalletsAsync(false, ct), Label);

        if (wallet is null)
            ConsoleUi.Pause();

        return wallet;
    }

    public static string Label(Wallet w) => $"{w.Name} ({ConsoleUi.Money(w.Balance, w.Currency)})";
}
