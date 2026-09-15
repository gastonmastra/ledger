namespace Ledger.Modules.Transactions.Domain;

public readonly record struct PostingId(Guid Value)
{
    public static PostingId New()
        => new(Guid.NewGuid());
}

public class Posting
{

    private Posting(
        PostingId id,
        TransactionId transactionId,
        AccountId accountId,
        decimal amount
    )
    {
        Id = id;
        TransactionId = transactionId;
        AccountId = accountId;
        Amount = amount;
    }

    public PostingId Id { get; private set; }
    public TransactionId TransactionId { get; private set; }
    public AccountId AccountId { get; private set; }
    public decimal Amount { get; private set; }
    
    internal static Posting Create(
        TransactionId transactionId,
        AccountId accountId,
        decimal amount
    )
    {
        if (amount == 0)
            throw new ArgumentException(
                "Posting amount cannot be zero.");
        return new Posting(
            PostingId.New(),
            transactionId,
            accountId,
            amount
        );
    }
}