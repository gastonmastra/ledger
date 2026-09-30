namespace Ledger.BuildingBlocks.Domain;

public static class Money
{
    /// <summary>Amounts entered by users have at most two decimals.</summary>
    public const int Decimals = 2;

    public static decimal Round(decimal amount)
        => Math.Round(amount, Decimals, MidpointRounding.ToEven);

    public static void EnsurePositive(decimal amount, string name = "amount")
    {
        if (amount <= 0)
            throw new DomainException(
                "money.not_positive",
                $"The {name} must be greater than zero.");

        EnsureCents(amount, name);
    }

    public static void EnsureCents(decimal amount, string name = "amount")
    {
        if (Round(amount) != amount)
            throw new DomainException(
                "money.too_many_decimals",
                $"The {name} can have at most {Decimals} decimals.");
    }
}
