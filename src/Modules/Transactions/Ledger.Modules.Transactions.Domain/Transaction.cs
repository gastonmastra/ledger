using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Transactions.Domain;

public readonly record struct TransactionId(Guid Value)
{
    public static TransactionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Why a transaction exists besides the original recording.</summary>
public enum ChangeAction
{
    /// <summary>The user corrected it.</summary>
    Edit,
    /// <summary>The user deleted it (only a reversal is posted).</summary>
    Delete,
    /// <summary>The user brought back a deleted one.</summary>
    Restore,
    /// <summary>A change made by the system (e.g. a rule migration).</summary>
    System
}

/// <summary>
/// Something that happened to the money of one user at a point in time. It is made of two or
/// more postings that add up to zero in every currency.
/// <para>
/// Posted transactions are never changed or removed. A correction posts a <b>reversal</b> of
/// the current revision (same date, opposite amounts) and, for an edit, a new <b>revision</b> of
/// the same entry. The only thing ever updated on a posted row is <see cref="ReversedById"/>.
/// </para>
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
        EntryId = id;
        Revision = 1;
        OwnerId = ownerId;
        OccurredAt = occurredAt;
        Kind = kind;
        Description = description;
        CorrelationId = correlationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public const int DescriptionMaxLength = 500;
    public const int ReasonMaxLength = 500;

    public TransactionId Id { get; private set; }

    /// <summary>The logical entry: the id of its first revision. All revisions and reversals share it.</summary>
    public TransactionId EntryId { get; private set; }

    public int Revision { get; private set; }
    public Guid OwnerId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public TransactionKind Kind { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Id of the business operation that produced the transaction (wallet, debt operation...).</summary>
    public Guid? CorrelationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set on reversals: the transaction they cancel.</summary>
    public TransactionId? ReversalOfId { get; private set; }

    /// <summary>Set when this transaction was cancelled by a reversal.</summary>
    public TransactionId? ReversedById { get; private set; }

    /// <summary>Who made the change (null for the system or the original recording).</summary>
    public Guid? ChangedByUserId { get; private set; }

    public ChangeAction? ChangeAction { get; private set; }
    public string? ChangeReason { get; private set; }

    public IReadOnlyCollection<Posting> Postings => _postings;

    public bool IsReversal => Kind is TransactionKind.Reversal or TransactionKind.OpeningBalanceReversal;

    /// <summary>Counts in lists and reports: not a reversal and not reversed.</summary>
    public bool IsCurrent => ReversedById is null && !IsReversal;

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

    /// <summary>Cancels <paramref name="original"/>: same date, opposite postings. Marks the original as reversed.</summary>
    public static Transaction ReversalOf(Transaction original, Guid? changedBy, ChangeAction action, string reason)
    {
        if (!original.IsCurrent)
            throw new DomainException("transaction.not_current", "Only the current version of a transaction can be changed.");

        var reversal = new Transaction(
            TransactionId.New(), original.OwnerId, original.OccurredAt, TransactionKind.Reversal,
            original.Description, original.CorrelationId)
        {
            EntryId = original.EntryId,
            Revision = original.Revision,
            ReversalOfId = original.Id
        };

        reversal.SetChange(changedBy, action, reason);

        foreach (var posting in original.Postings)
            reversal.AddPosting(posting.AccountId, -posting.Amount, posting.Currency, posting.CounterpartyId);

        reversal.Validate();
        original.ReversedById = reversal.Id;

        return reversal;
    }

    /// <summary>Turns a freshly built transaction into the next revision of <paramref name="previous"/>'s entry.</summary>
    public void BecomeRevisionOf(Transaction previous, int revision, Guid? changedBy, ChangeAction action, string reason)
    {
        EntryId = previous.EntryId;
        Revision = revision;
        SetChange(changedBy, action, reason);
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

    private void SetChange(Guid? changedBy, ChangeAction action, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("transaction.reason_required", "A reason is required to change a transaction.");

        reason = reason.Trim();

        if (reason.Length > ReasonMaxLength)
            throw new DomainException(
                "transaction.reason_too_long", $"The reason cannot be longer than {ReasonMaxLength} characters.");

        ChangedByUserId = changedBy;
        ChangeAction = action;
        ChangeReason = reason;
    }
}
