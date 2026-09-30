namespace Ledger.BuildingBlocks.Domain;

public enum Currency
{
    EUR,
    USD,
    ARS
}

public static class Currencies
{
    public static Currency Parse(string value)
    {
        if (!Enum.TryParse<Currency>(value, ignoreCase: true, out var currency)
            || !Enum.IsDefined(currency))
            throw new DomainException(
                "currency.unsupported",
                $"Currency '{value}' is not supported. Use one of: {string.Join(", ", Enum.GetNames<Currency>())}.");

        return currency;
    }

    public static bool IsSupported(string? value)
        => value is not null
           && Enum.TryParse<Currency>(value, ignoreCase: true, out var currency)
           && Enum.IsDefined(currency);
}
