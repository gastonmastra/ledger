using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Investments.Domain;

/// <summary>Quantities of an asset: positive, with up to 12 decimals (most assets are bought in fractions).</summary>
public static class Quantities
{
    public const int Decimals = 12;
    public const int Precision = 28;

    public static void EnsureValid(decimal quantity)
    {
        if (quantity <= 0)
            throw new DomainException("investment.quantity_not_positive", "The quantity must be greater than zero.");

        if (Math.Round(quantity, Decimals) != quantity)
            throw new DomainException(
                "investment.quantity_too_many_decimals", $"The quantity can have at most {Decimals} decimals.");
    }
}

/// <summary>Unit prices of an asset: positive, with up to 8 decimals (some assets cost less than a cent).</summary>
public static class Prices
{
    public const int Decimals = 8;
    public const int Precision = 19;

    public static void EnsureValid(decimal price)
    {
        if (price <= 0)
            throw new DomainException("investment.price_not_positive", "The price must be greater than zero.");

        if (Math.Round(price, Decimals) != price)
            throw new DomainException(
                "investment.price_too_many_decimals", $"The price can have at most {Decimals} decimals.");
    }
}
