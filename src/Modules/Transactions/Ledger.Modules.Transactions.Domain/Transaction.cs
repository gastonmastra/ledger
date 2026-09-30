using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Transactions.Domain;

/// <summary>
/// Something that happened to the money of one user at a point in time. It is made of two or
/// more postings that add up to zero in every currency.
/// </summary>
public sealed class Transaction
{
    private readonly List<Posting> _postings = [];

    private Transaction(
        TransactionId id,
        Guid ownerId,
        DateTimeOffset occurredAt,
        TransactionKind kind,
        string? description,
        Guid? correlationId)
    {
        Id = id;
        OwnerId = ownerId;
        OccurredAt = occurredAt;
        Kind = kind;
        Description = description;
        CorrelationId = correlationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public const int DescriptionMaxLength = 500;

    public TransactionId Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public TransactionKind Kind { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Id of the business operation that produced the transaction (wallet, debt operation...).</summary>
    public Guid? CorrelationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<Posting> Postings => _postings;

    public static Transaction Create(
        Guid ownerId,
        DateTimeOffset occurredAt,
        TransactionKind kind,
        string? description,
        Guid? correlationId)
    {
        if (description?.Length > DescriptionMaxLength)
            throw new DomainException(
                "transaction.description_too_long",
                $"Description cannot be longer than {DescriptionMaxLength} characters.");

        return new Transaction(
            TransactionId.New(),
            ownerId,
            occurredAt,
            kind,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            correlationId);
    }

    public void AddPosting(AccountId accountId, decimal amount, Currency currency, Guid? counterpartyId = null)
        => _postings.Add(Posting.Create(Id, accountId, amount, currency, counterpartyId));

    public void Validate()
    {
        if (_postings.Count < 2)
            throw new DomainException(
                "transaction.too_few_postings",
                "A transaction must have at least two postings.");

        var unbalanced = _postings
            .GroupBy(x => x.Currency)
            .Where(g => g.Sum(x => x.Amount) != 0)
            .Select(g => g.Key)
            .ToList();

        if (unbalanced.Count != 0)
            throw new DomainException(
                "transaction.unbalanced",
                $"Transaction postings must balance in every currency (unbalanced: {string.Join(", ", unbalanced)}).");
    }
}
