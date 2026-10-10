using Ledger.Console.Api;
using SystemConsole = System.Console;

namespace Ledger.Console.Menus;

public sealed class SummaryScreen(LedgerApiClient api)
{
    public async Task ShowAsync(CancellationToken ct)
    {
        ConsoleUi.Header("MY MONEY");

        var summary = await api.SummaryAsync(ct);
        var investments = await api.InvestmentsSummaryAsync(ct);

        ConsoleUi.Table(summary.Totals,
            ("Currency", t => t.Currency),
            ("Wallets", t => t.Wallets),
            ("Owed to me", t => t.OwedToMe),
            ("I owe", t => t.IOwe),
            ("Pending", t => t.Pending),
            ("Adjustments", t => t.Adjustments),
            ("Net", t => t.Net));

        if (investments.Count > 0)
        {
            // Investments are valued at the latest sell price; "Net" above is money only.
            SystemConsole.WriteLine("\nInvestments (market value) and total:");
            ConsoleUi.Table(investments,
                ("Currency", i => i.Currency),
                ("Investments", i => i.Value),
                ("Cost", i => i.Cost),
                ("Unrealized", i => i.UnrealizedGain),
                ("Total", i => i.Value + (summary.Totals.FirstOrDefault(t => t.Currency == i.Currency)?.Net ?? 0)));
        }

        SystemConsole.WriteLine("\nWallets:");
        ConsoleUi.Table(summary.Wallets, ("Wallet", w => w.Name), ("Balance", w => ConsoleUi.Money(w.Balance, w.Currency)));

        SystemConsole.WriteLine("\nPeople and groups:");
        ConsoleUi.Table(summary.Counterparties,
            ("With", c => c.Kind == "Group" ? $"{c.Name} (group)" : c.Name),
            ("Owes me", c => ConsoleUi.Money(c.OwedToMe, c.Currency)),
            ("I owe", c => ConsoleUi.Money(c.IOwe, c.Currency)));

        ConsoleUi.Pause();
    }
}
