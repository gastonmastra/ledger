using System.Globalization;
using Ledger.Console.Api;
using SystemConsole = System.Console;

namespace Ledger.Console.Menus;

/// <summary>Assets, holdings valued at the latest sell price, buys, sales, owned holdings and prices.</summary>
public sealed class InvestmentsMenu(LedgerApiClient api) : Menu
{
    private static readonly string[] Currencies = ["EUR", "USD", "ARS"];
    private static readonly string[] Types = ["Stock", "Etf", "Crypto", "Bond", "Fund", "Other"];

    protected override string Title => "INVESTMENTS";

    protected override IReadOnlyList<(string, Func<CancellationToken, Task>)> Actions =>
    [
        ("Holdings", HoldingsAsync),
        ("Buy", ct => TradeAsync("buy", ct)),
        ("Sell", ct => TradeAsync("sell", ct)),
        ("Add a holding I already own", ct => TradeAsync("opening", ct)),
        ("Update price", PriceAsync),
        ("Trades", TradesAsync),
        ("Price history of an asset", PricesAsync),
        ("New asset", NewAssetAsync),
        ("Edit asset", EditAsync),
        ("Archive asset", ArchiveAsync)
    ];

    private async Task HoldingsAsync(CancellationToken ct)
    {
        foreach (var total in await api.InvestmentsSummaryAsync(ct))
        {
            SystemConsole.WriteLine(
                $"{total.Currency}: value {ConsoleUi.Money(total.Value, total.Currency)} · cost {ConsoleUi.Money(total.Cost, total.Currency)}" +
                $" · unrealized {Signed(total.UnrealizedGain, total.Currency)} · realized {Signed(total.RealizedGain, total.Currency)}");
        }

        SystemConsole.WriteLine();
        ConsoleUi.Table(await api.AssetsAsync(includeArchived: false, ct),
            ("Asset", a => Label(a)),
            ("Quantity", a => Quantity(a.Quantity)),
            ("Avg cost", a => a.AverageCost is { } c ? Price(c, a.Currency) : ""),
            ("Valued at", a => a.ValuationPrice is { } p ? $"{Price(p, a.Currency)} ({a.ValuationSource})" : ""),
            ("Value", a => ConsoleUi.Money(a.MarketValue, a.Currency)),
            ("Unrealized", a => a.Quantity > 0 ? Signed(a.UnrealizedGain, a.Currency) : ""),
            ("Status", a => a.HasPendingTrade ? "Being recorded" : a.Status));

        ConsoleUi.Pause();
    }

    private async Task TradeAsync(string kind, CancellationToken ct)
    {
        var assets = await api.AssetsAsync(includeArchived: false, ct);
        Asset? asset;

        if (kind == "sell")
        {
            asset = ConsoleUi.Choose("Asset", assets.Where(a => a.Quantity > 0).ToList(), a => $"{Label(a)} · {Quantity(a.Quantity)}");
        }
        else
        {
            var choices = assets.Select(a => (Asset?)a).Append(null).ToList();
            var picked = ConsoleUi.Choose("Asset", choices.Select(a => new AssetChoice(a)).ToList(), c => c.Label);
            asset = picked is null ? null : picked.Asset ?? await CreateAssetAsync(ct);
        }

        if (asset is null)
        {
            ConsoleUi.Pause();
            return;
        }

        if (asset.LastTradeAt is { } last)
            SystemConsole.WriteLine($"Latest trade: {last.ToLocalTime():yyyy-MM-dd HH:mm} (new trades can't be dated before it).");

        var latest = kind == "sell" ? asset.SellPrice : asset.BuyPrice;
        var quantity = ConsoleUi.ReadDecimal(kind == "sell" ? $"Quantity (you hold {Quantity(asset.Quantity)}): " : "Quantity: ");
        var unitPrice = ConsoleUi.ReadDecimal(
            (kind == "sell" ? "Price per unit received" : "Price per unit paid") +
            (latest is { } l ? $" (latest {Price(l, asset.Currency)})" : "") + $" in {asset.Currency}: ");
        var total = Math.Round(quantity * unitPrice, 2, MidpointRounding.ToEven);
        SystemConsole.WriteLine($"Total: {ConsoleUi.Money(total, asset.Currency)}");

        Guid? walletId = null;
        decimal? walletAmount = null;

        if (kind != "opening")
        {
            var wallet = ConsoleUi.Choose(kind == "sell" ? "Into wallet" : "From wallet", await api.WalletsAsync(false, ct), WalletsMenu.Label);
            if (wallet is null)
            {
                ConsoleUi.Pause();
                return;
            }

            walletId = wallet.Id;

            if (wallet.Currency != asset.Currency)
                walletAmount = ConsoleUi.ReadDecimal($"The wallet holds {wallet.Currency}: amount {(kind == "sell" ? "received" : "paid")} in {wallet.Currency}: ");
        }

        var counterPrice = ConsoleUi.ReadOptionalDecimal(
            kind == "sell" ? "Current buy price (optional, ENTER to skip): " : "Current sell price (optional, ENTER to skip): ");
        var description = ConsoleUi.ReadText("Description (optional): ", optional: true);
        var when = ConsoleUi.ReadWhen();

        var trade = await api.WaitForTradeAsync(
            await api.RecordTradeAsync(kind, asset.Id, quantity, unitPrice, walletId, walletAmount, counterPrice, when,
                description.Length == 0 ? null : description, ct),
            ct);

        switch (trade.Status)
        {
            case "Completed":
                ConsoleUi.Success(trade.Kind == "Sell"
                    ? $"Sale recorded. Realized gain: {Signed(trade.RealizedGain, trade.Currency)}."
                    : trade.Kind == "Buy" ? "Purchase recorded." : "Holding recorded.");
                break;
            case "Rejected":
                ConsoleUi.Error($"Rejected: {trade.RejectionReason}");
                ConsoleUi.Pause();
                break;
            default:
                ConsoleUi.Success($"Still being recorded (trade {trade.Id}). Check the holdings in a moment.");
                break;
        }
    }

    private async Task PriceAsync(CancellationToken ct)
    {
        if (await PickAsync(ct) is not { } asset)
            return;

        SystemConsole.WriteLine("Holdings are valued at the sell price (what you would get). No money moves.");
        var sell = ConsoleUi.ReadOptionalDecimal($"Sell price{Current(asset.SellPrice, asset)} (ENTER to skip): ");
        var buy = ConsoleUi.ReadOptionalDecimal($"Buy price{Current(asset.BuyPrice, asset)} (ENTER to skip): ");

        if (sell is null && buy is null)
        {
            ConsoleUi.Error("Enter at least one price.");
            ConsoleUi.Pause();
            return;
        }

        await api.RecordPriceAsync(asset.Id, buy, sell, ConsoleUi.ReadWhen(), ct);
        ConsoleUi.Success("Price recorded.");
    }

    private async Task TradesAsync(CancellationToken ct)
    {
        var trades = await api.TradesAsync(null, 1, ct);

        ConsoleUi.Table(trades.Items,
            ("When", t => t.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            ("Asset", t => t.AssetSymbol ?? t.AssetName),
            ("Trade", t => t.Kind == "Opening" ? "Already owned" : t.Kind),
            ("Quantity", t => Quantity(t.Quantity)),
            ("Price", t => Price(t.UnitPrice, t.Currency)),
            ("Total", t => ConsoleUi.Money(t.Total, t.Currency) + (t.WalletAmount is { } w ? $" ({ConsoleUi.Money(w, t.WalletCurrency!)})" : "")),
            ("Realized", t => t.Kind == "Sell" ? Signed(t.RealizedGain, t.Currency) : ""),
            ("Status", t => t.Status == "Rejected" ? $"Rejected: {t.RejectionReason}" : t.Status));

        if (trades.TotalCount > trades.Items.Count)
            SystemConsole.WriteLine($"\n(latest {trades.Items.Count} of {trades.TotalCount})");

        ConsoleUi.Pause();
    }

    private async Task PricesAsync(CancellationToken ct)
    {
        if (await PickAsync(ct, includeArchived: true) is not { } asset)
            return;

        ConsoleUi.Table(await api.AssetPricesAsync(asset.Id, ct),
            ("When", p => p.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            ("Side", p => p.Side),
            ("Price", p => Price(p.Price, asset.Currency)),
            ("From", p => p.Source));

        ConsoleUi.Pause();
    }

    private async Task NewAssetAsync(CancellationToken ct)
    {
        await CreateAssetAsync(ct);
        ConsoleUi.Pause();
    }

    private async Task<Asset> CreateAssetAsync(CancellationToken ct)
    {
        var name = ConsoleUi.ReadText("Name: ");
        var symbol = ConsoleUi.ReadText("Symbol (optional): ", optional: true);
        var type = ConsoleUi.Choose("Type", Types, t => t)!;
        var currency = ConsoleUi.Choose("Quoted in (the currency of its price)", Currencies, c => c)!;

        var asset = await api.CreateAssetAsync(name, symbol.Length == 0 ? null : symbol, type, currency, ct);
        SystemConsole.WriteLine($"Asset '{asset.Name}' added.");

        return asset;
    }

    private async Task EditAsync(CancellationToken ct)
    {
        if (await PickAsync(ct) is not { } asset)
            return;

        var name = ConsoleUi.ReadTextOrKeep("Name", asset.Name) ?? asset.Name;
        var symbol = ConsoleUi.ReadTextOrKeep("Symbol", asset.Symbol);

        await api.UpdateAssetAsync(asset.Id, name, symbol, ct);
        ConsoleUi.Success("Asset updated.");
    }

    private async Task ArchiveAsync(CancellationToken ct)
    {
        if (await PickAsync(ct) is not { } asset)
            return;

        await api.ArchiveAssetAsync(asset.Id, ct);
        ConsoleUi.Success($"Asset '{asset.Name}' archived.");
    }

    private async Task<Asset?> PickAsync(CancellationToken ct, bool includeArchived = false)
    {
        var asset = ConsoleUi.Choose("Asset", await api.AssetsAsync(includeArchived, ct), a => $"{Label(a)} · {Quantity(a.Quantity)}");

        if (asset is null)
            ConsoleUi.Pause();

        return asset;
    }

    private static string Label(Asset a) => a.Symbol is null ? $"{a.Name} ({a.Currency})" : $"{a.Name} {a.Symbol} ({a.Currency})";

    private static string Quantity(decimal quantity) => quantity.ToString("0.############", CultureInfo.InvariantCulture);

    private static string Price(decimal price, string currency)
        => $"{price.ToString("#,##0.00######", CultureInfo.InvariantCulture)} {currency}";

    private static string Signed(decimal amount, string currency) => (amount > 0 ? "+" : "") + ConsoleUi.Money(amount, currency);

    private static string Current(decimal? price, Asset asset) => price is { } p ? $" [current {Price(p, asset.Currency)}]" : "";

    /// <summary>An existing asset, or (null) "new asset".</summary>
    private sealed record AssetChoice(Asset? Asset)
    {
        public string Label => Asset is null ? "+ New asset" : $"{InvestmentsMenu.Label(Asset)} · {Quantity(Asset.Quantity)}";
    }
}
