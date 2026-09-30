namespace Ledger.Modules.Transactions.Domain;

public readonly record struct PostingId(Guid Value)
{
    public static PostingId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct AccountId(Guid Value)
{
    public override string ToString() => Value.ToString();
}
