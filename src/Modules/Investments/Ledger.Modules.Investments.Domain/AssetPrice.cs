using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Investments.Domain;

public enum PriceSide
{
    /// <summary>What you pay to buy one unit.</summary>
    Buy,
    /// <summary>What you get when selling one unit: what the holdings are worth.</summary>
    Sell
}

public enum PriceSource
{
    Trade,
    Manual
}

/// <summary>A known price of an asset at a moment. Prices only value holdings: no money moves.</summary>
public sealed class AssetPrice
{
    private AssetPrice()
    {
    }

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid OwnerId { get; private set; }
    public PriceSide Side { get; private set; }
    public decimal Price { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public PriceSource Source { get; private set; }
    public Guid? TradeId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static AssetPrice Manual(Asset asset, PriceSide side, decimal price, DateTimeOffset occurredAt)
    {
        if (asset.Status == AssetStatus.Archived)
            throw new DomainException("investment.archived", $"'{asset.Name}' is archived.");

        Prices.EnsureValid(price);

        return Create(asset.Id, asset.OwnerId, side, price, occurredAt, PriceSource.Manual, null);
    }

    internal static AssetPrice FromTrade(Trade trade, PriceSide side, decimal price)
        => Create(trade.AssetId, trade.OwnerId, side, price, trade.OccurredAt, PriceSource.Trade, trade.Id);

    private static AssetPrice Create(
        Guid assetId, Guid ownerId, PriceSide side, decimal price, DateTimeOffset occurredAt, PriceSource source, Guid? tradeId)
        => new()
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            OwnerId = ownerId,
            Side = side,
            Price = price,
            OccurredAt = occurredAt,
            Source = source,
            TradeId = tradeId,
            CreatedAt = DateTimeOffset.UtcNow
        };
}
