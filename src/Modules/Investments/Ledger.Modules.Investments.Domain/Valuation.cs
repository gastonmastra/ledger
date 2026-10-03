using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Investments.Domain;

public enum ValuationSource
{
    /// <summary>The latest known sell price: what the holding would fetch.</summary>
    Sell,
    /// <summary>No sell price known yet: the latest buy price.</summary>
    Buy,
    /// <summary>No price known: the average cost.</summary>
    Cost
}

public readonly record struct ValuationPrice(decimal Price, ValuationSource Source);

/// <summary>How a holding is valued at a moment: quantity × the price that matters to the owner.</summary>
public static class Valuation
{
    /// <summary>
    /// Latest sell price before <paramref name="before"/>, else the latest buy price, else the average cost.
    /// Null when there is nothing to value it with.
    /// </summary>
    public static ValuationPrice? PriceAt(
        IEnumerable<AssetPrice> prices,
        DateTimeOffset before,
        decimal quantity,
        decimal costBasis)
    {
        AssetPrice? sell = null, buy = null;

        foreach (var price in prices.Where(p => p.OccurredAt < before))
        {
            if (price.Side == PriceSide.Sell && (sell is null || IsLater(price, sell)))
                sell = price;
            else if (price.Side == PriceSide.Buy && (buy is null || IsLater(price, buy)))
                buy = price;
        }

        if (sell is not null)
            return new ValuationPrice(sell.Price, ValuationSource.Sell);

        if (buy is not null)
            return new ValuationPrice(buy.Price, ValuationSource.Buy);

        return quantity == 0 ? null : new ValuationPrice(Math.Round(costBasis / quantity, Prices.Decimals), ValuationSource.Cost);
    }

    /// <summary>Market value to the cent. Valued at cost, it is exactly the cost basis.</summary>
    public static decimal Value(decimal quantity, decimal costBasis, ValuationPrice? price)
        => price switch
        {
            null => 0,
            { Source: ValuationSource.Cost } => costBasis,
            { } p => Money.Round(quantity * p.Price)
        };

    private static bool IsLater(AssetPrice candidate, AssetPrice current)
        => candidate.OccurredAt > current.OccurredAt
           || (candidate.OccurredAt == current.OccurredAt && candidate.CreatedAt > current.CreatedAt);
}
