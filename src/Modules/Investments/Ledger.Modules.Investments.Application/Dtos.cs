using Ledger.Modules.Investments.Domain;

namespace Ledger.Modules.Investments.Application;

/// <summary>An asset and the user's holding of it, valued now.</summary>
/// <param name="AverageCost">Cost per unit owned; null when nothing is owned.</param>
/// <param name="BuyPrice">Latest known buy price.</param>
/// <param name="SellPrice">Latest known sell price.</param>
/// <param name="ValuationPrice">Price used to value the holding (sell, else buy, else average cost).</param>
/// <param name="MarketValue">Quantity × valuation price.</param>
/// <param name="UnrealizedGain">MarketValue − CostBasis.</param>
public sealed record AssetDto(
    Guid Id,
    string Name,
    string? Symbol,
    AssetType Type,
    string Currency,
    AssetStatus Status,
    decimal Quantity,
    decimal CostBasis,
    decimal? AverageCost,
    decimal? BuyPrice,
    decimal? SellPrice,
    decimal? ValuationPrice,
    ValuationSource? ValuationSource,
    decimal MarketValue,
    decimal UnrealizedGain,
    decimal RealizedGain,
    DateTimeOffset? LastTradeAt,
    bool HasPendingTrade,
    DateTimeOffset CreatedAt);

public sealed record TradeDto(
    Guid Id,
    Guid AssetId,
    string AssetName,
    string? AssetSymbol,
    TradeKind Kind,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total,
    string Currency,
    Guid? WalletId,
    string? WalletCurrency,
    decimal? WalletAmount,
    decimal CostRemoved,
    decimal RealizedGain,
    decimal? CounterPrice,
    DateTimeOffset OccurredAt,
    string? Description,
    TradeStatus Status,
    string? RejectionReason,
    DateTimeOffset CreatedAt);

public sealed record AssetPriceDto(
    Guid Id,
    PriceSide Side,
    decimal Price,
    DateTimeOffset OccurredAt,
    PriceSource Source,
    Guid? TradeId);

internal static class DtoMapping
{
    public static AssetDto ToDto(this Asset asset, IReadOnlyCollection<AssetPrice> prices, DateTimeOffset now)
    {
        var valuation = Valuation.PriceAt(prices, now.AddTicks(1), asset.Quantity, asset.CostBasis);
        var value = Valuation.Value(asset.Quantity, asset.CostBasis, valuation);

        return new AssetDto(
            asset.Id,
            asset.Name,
            asset.Symbol,
            asset.Type,
            asset.Currency.ToString(),
            asset.Status,
            asset.Quantity,
            asset.CostBasis,
            asset.AverageCost,
            Latest(prices, PriceSide.Buy),
            Latest(prices, PriceSide.Sell),
            valuation?.Price,
            valuation?.Source,
            value,
            value - asset.CostBasis,
            asset.RealizedGain,
            asset.LastTradeAt,
            asset.PendingTradeId is not null,
            asset.CreatedAt);
    }

    public static TradeDto ToDto(this Trade trade, Asset asset)
        => new(
            trade.Id,
            trade.AssetId,
            asset.Name,
            asset.Symbol,
            trade.Kind,
            trade.Quantity,
            trade.UnitPrice,
            trade.Total,
            trade.Currency.ToString(),
            trade.WalletId,
            trade.WalletCurrency?.ToString(),
            trade.WalletAmount,
            trade.CostRemoved,
            trade.RealizedGain,
            trade.CounterPrice,
            trade.OccurredAt,
            trade.Description,
            trade.Status,
            trade.RejectionReason,
            trade.CreatedAt);

    public static AssetPriceDto ToDto(this AssetPrice price)
        => new(price.Id, price.Side, price.Price, price.OccurredAt, price.Source, price.TradeId);

    private static decimal? Latest(IEnumerable<AssetPrice> prices, PriceSide side)
        => prices.Where(p => p.Side == side)
            .OrderByDescending(p => p.OccurredAt).ThenByDescending(p => p.CreatedAt)
            .Select(p => (decimal?)p.Price)
            .FirstOrDefault();
}
