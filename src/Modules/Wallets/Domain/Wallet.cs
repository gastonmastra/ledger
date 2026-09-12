namespace Ledger.Modules.Wallets.Domain;

public class Wallet
{
    private Wallet(
        Guid id,
        Guid userId,
        string name,
        string currency
    )
    {
        Id = id;
        UserId = userId;
        Name = name;
        Currency = currency;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; }
    public string Currency { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static Wallet Create(
        Guid userId,
        string name,
        string currency
    )
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Wallet name is required.");

        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.");

        return new Wallet(
            Guid.NewGuid(),
            userId,
            name,
            currency
        );
    }
}