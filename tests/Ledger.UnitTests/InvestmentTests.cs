using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Investments.Domain;

namespace Ledger.UnitTests;

public class InvestmentTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Wallet = Guid.NewGuid();

    private static Asset NewAsset() => Asset.Create(Guid.NewGuid(), "Bitcoin", " btc ", AssetType.Crypto, Currency.USD);

    private static Trade Record(
        Asset asset,
        TradeKind kind,
        decimal quantity,
        decimal price,
        DateTimeOffset? at = null,
        decimal? counterPrice = null)
    {
        var trade = asset.StartTrade(
            kind, quantity, price, at ?? Day1, null, kind == TradeKind.Opening ? null : Wallet, null, counterPrice);

        trade.Complete(kind == TradeKind.Opening ? null : Currency.USD);
        asset.Apply(trade);

        return trade;
    }

    [Fact]
    public void Symbols_are_trimmed_and_upper_case()
        => Assert.Equal("BTC", NewAsset().Symbol);

    [Fact]
    public void Buying_adds_the_quantity_and_the_rounded_total_to_the_cost()
    {
        var asset = NewAsset();

        var trade = Record(asset, TradeKind.Buy, 0.123456789012m, 50_000.12345678m);

        Assert.Equal(Money.Round(0.123456789012m * 50_000.12345678m), trade.Total);
        Assert.Equal(0.123456789012m, asset.Quantity);
        Assert.Equal(trade.Total, asset.CostBasis);
        Assert.Equal(Day1, asset.LastTradeAt);
        Assert.Null(asset.PendingTradeId);
    }

    [Fact]
    public void Selling_removes_the_average_cost_and_realizes_the_gain()
    {
        var asset = NewAsset();
        Record(asset, TradeKind.Buy, 1, 100);
        Record(asset, TradeKind.Buy, 2, 130); // 3 units cost 360: average 120

        var sale = Record(asset, TradeKind.Sell, 1, 150, Day1.AddDays(1));

        Assert.Equal(120, sale.CostRemoved);
        Assert.Equal(30, sale.RealizedGain);
        Assert.Equal(2, asset.Quantity);
        Assert.Equal(240, asset.CostBasis);
        Assert.Equal(30, asset.RealizedGain);
        Assert.Equal(120, asset.AverageCost);
    }

    [Fact]
    public void Selling_at_a_loss_realizes_a_negative_gain()
    {
        var asset = NewAsset();
        Record(asset, TradeKind.Buy, 2, 100);

        var sale = Record(asset, TradeKind.Sell, 1, 80);

        Assert.Equal(-20, sale.RealizedGain);
        Assert.Equal(-20, asset.RealizedGain);
    }

    [Fact]
    public void Selling_everything_removes_the_whole_cost_basis()
    {
        var asset = NewAsset();
        Record(asset, TradeKind.Buy, 3, 33.333m); // 99.999 → 100.00
        Record(asset, TradeKind.Sell, 1, 40);      // removes 33.33

        var last = Record(asset, TradeKind.Sell, 2, 40);

        Assert.Equal(66.67m, last.CostRemoved);
        Assert.Equal(0, asset.Quantity);
        Assert.Equal(0, asset.CostBasis);
        Assert.Null(asset.AverageCost);
    }

    [Fact]
    public void Cannot_sell_more_than_held()
    {
        var asset = NewAsset();
        Record(asset, TradeKind.Opening, 1, 100);

        var error = Assert.Throws<DomainException>(() =>
            asset.StartTrade(TradeKind.Sell, 1.5m, 100, Day1, null, Wallet, null, null));

        Assert.Equal("investment.insufficient_quantity", error.Code);
    }

    [Fact]
    public void Trades_cannot_be_dated_before_the_latest_trade()
    {
        var asset = NewAsset();
        Record(asset, TradeKind.Buy, 1, 100);

        var error = Assert.Throws<DomainException>(() =>
            asset.StartTrade(TradeKind.Buy, 1, 100, Day1.AddMinutes(-1), null, Wallet, null, null));

        Assert.Equal("investment.backdated", error.Code);

        // Same moment is fine.
        asset.StartTrade(TradeKind.Buy, 1, 100, Day1, null, Wallet, null, null);
    }

    [Fact]
    public void Only_one_trade_per_asset_is_pending_and_a_rejection_releases_it()
    {
        var asset = NewAsset();
        var first = asset.StartTrade(TradeKind.Buy, 1, 100, Day1, null, Wallet, null, null);

        var error = Assert.Throws<DomainException>(() =>
            asset.StartTrade(TradeKind.Buy, 1, 100, Day1, null, Wallet, null, null));
        Assert.Equal("investment.trade_pending", error.Code);

        Assert.True(first.Reject("Not enough money"));
        asset.Release(first);

        Assert.Null(asset.PendingTradeId);
        Assert.Equal(0, asset.Quantity);
        Assert.False(first.Complete(Currency.USD)); // answered once
        asset.StartTrade(TradeKind.Buy, 1, 100, Day1, null, Wallet, null, null);
    }

    [Fact]
    public void Precision_rules()
    {
        var asset = NewAsset();

        Assert.Equal("investment.quantity_too_many_decimals", Assert.Throws<DomainException>(() =>
            asset.StartTrade(TradeKind.Buy, 0.0000000000001m, 100, Day1, null, Wallet, null, null)).Code);
        Assert.Equal("investment.price_too_many_decimals", Assert.Throws<DomainException>(() =>
            asset.StartTrade(TradeKind.Buy, 1, 0.000000001m, Day1, null, Wallet, null, null)).Code);
        Assert.Equal("investment.total_zero", Assert.Throws<DomainException>(() =>
            asset.StartTrade(TradeKind.Buy, 1, 0.001m, Day1, null, Wallet, null, null)).Code);

        // Sub-cent prices are fine when the total reaches a cent.
        var trade = asset.StartTrade(TradeKind.Buy, 1_000_000, 0.00001234m, Day1, null, Wallet, null, null);
        Assert.Equal(12.34m, trade.Total);
    }

    [Fact]
    public void Buying_and_selling_need_a_wallet_but_an_opening_holding_does_not()
    {
        var asset = NewAsset();

        Assert.Equal("investment.wallet_required", Assert.Throws<DomainException>(() =>
            asset.StartTrade(TradeKind.Buy, 1, 100, Day1, null, null, null, null)).Code);
        Assert.Equal("investment.opening_with_wallet", Assert.Throws<DomainException>(() =>
            asset.StartTrade(TradeKind.Opening, 1, 100, Day1, null, Wallet, null, null)).Code);
    }

    [Fact]
    public void A_wallet_amount_in_the_same_currency_is_dropped_when_recorded()
    {
        var asset = NewAsset();
        var sameCurrency = asset.StartTrade(TradeKind.Buy, 1, 100, Day1, null, Wallet, 95, null);
        sameCurrency.Complete(Currency.USD);
        Assert.Null(sameCurrency.WalletAmount);

        var other = NewAsset();
        var crossCurrency = other.StartTrade(TradeKind.Buy, 1, 100, Day1, null, Wallet, 120_000, null);
        crossCurrency.Complete(Currency.ARS);
        Assert.Equal(120_000, crossCurrency.WalletAmount);
        Assert.Equal(Currency.ARS, crossCurrency.WalletCurrency);
    }

    [Fact]
    public void A_trade_records_its_price_and_the_optional_counter_price()
    {
        var asset = NewAsset();

        var buy = Record(asset, TradeKind.Buy, 1, 101, counterPrice: 99);
        var prices = buy.Prices().ToList();

        Assert.Contains(prices, p => p is { Side: PriceSide.Buy, Price: 101, Source: PriceSource.Trade });
        Assert.Contains(prices, p => p is { Side: PriceSide.Sell, Price: 99 });

        var sale = Record(asset, TradeKind.Sell, 1, 98);
        Assert.Single(sale.Prices(), p => p.Side == PriceSide.Sell && p.Price == 98);
    }

    [Fact]
    public void Valuation_uses_the_sell_price_then_the_buy_price_then_the_cost()
    {
        var asset = NewAsset();
        var buy = Record(asset, TradeKind.Buy, 2, 100);
        var now = Day1.AddDays(10);

        Assert.Equal(new ValuationPrice(50, ValuationSource.Cost), Valuation.PriceAt([], now, 2, 100));

        var prices = buy.Prices().ToList();
        Assert.Equal(new ValuationPrice(100, ValuationSource.Buy), Valuation.PriceAt(prices, now, 2, 200));

        prices.Add(AssetPrice.Manual(asset, PriceSide.Sell, 90, Day1.AddDays(1)));
        prices.Add(AssetPrice.Manual(asset, PriceSide.Buy, 120, Day1.AddDays(2)));
        var price = Valuation.PriceAt(prices, now, 2, 200);

        Assert.Equal(new ValuationPrice(90, ValuationSource.Sell), price);
        Assert.Equal(180, Valuation.Value(2, 200, price));

        // Before the sell price was known, the buy price values it.
        Assert.Equal(ValuationSource.Buy, Valuation.PriceAt(prices, Day1.AddHours(1), 2, 200)!.Value.Source);
        Assert.Null(Valuation.PriceAt([], now, 0, 0));
    }

    [Fact]
    public void Only_assets_no_longer_held_can_be_archived()
    {
        var asset = NewAsset();
        Record(asset, TradeKind.Buy, 1, 100);

        Assert.Equal("investment.not_empty", Assert.Throws<DomainException>(asset.Archive).Code);

        Record(asset, TradeKind.Sell, 1, 100);
        asset.Archive();

        Assert.Equal("investment.archived", Assert.Throws<DomainException>(() =>
            asset.StartTrade(TradeKind.Buy, 1, 100, Day1, null, Wallet, null, null)).Code);
    }
}
