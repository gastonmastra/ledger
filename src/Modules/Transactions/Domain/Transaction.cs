namespace Ledger.Modules.Transactions.Domain;

public readonly record struct TransactionId(Guid Value)
{
    public static TransactionId New()
        => new(Guid.NewGuid());

    public override string ToString()
        => Value.ToString();
}

public class Transaction
{
    private readonly List<Posting> _postings = [];
    private Transaction(
        TransactionId id,
        DateTimeOffset occurredAt,
        string? description
    )
    {
        Id = id;
        OccurredAt = occurredAt;
        Description = description;
    }
    public TransactionId Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Description { get; private set; }
    public IReadOnlyCollection<Posting> Postings => _postings;

    public static Transaction Create(
        DateTimeOffset occurredAt,
        string? description
    )
    {
        return new Transaction(
            TransactionId.New(),
            occurredAt,
            description
        );
    }

    public void AddPosting(
        AccountId accountId,
        decimal amount
    )
    {
        _postings.Add(
            Posting.Create(
                Id,
                accountId,
                amount
            )
        );
    }

    public void Validate()
    {
        if (_postings.Count < 2)
            throw new InvalidOperationException(
                "A transaction must have at least two postings."
            );

        var total = _postings.Sum(x => x.Amount);

        if (total != 0)
            throw new InvalidOperationException(
                "Transaction postings must balance"
            );
    }
}