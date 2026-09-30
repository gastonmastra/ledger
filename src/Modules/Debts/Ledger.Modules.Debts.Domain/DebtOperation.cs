using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Debts.Domain;

/// <summary>
/// A loan, split expense or settlement. It is Pending until the ledger records it for every
/// registered user involved, then Completed (or Rejected with a reason).
/// </summary>
public sealed class DebtOperation
{
    public const int DescriptionMaxLength = 500;

    private readonly List<OperationPayer> _payers = [];
    private readonly List<OperationShare> _shares = [];

    private DebtOperation()
    {
    }

    public Guid Id { get; private set; }
    public OperationKind Kind { get; private set; }
    public OperationStatus Status { get; private set; }
    public Guid InitiatorUserId { get; private set; }
    public Guid? GroupId { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Currency Currency { get; private set; }
    public decimal Total { get; private set; }

    /// <summary>Split only.</summary>
    public SplitMethod? Method { get; private set; }

    /// <summary>Split only: the initiator's expense category.</summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>Settlement only.</summary>
    public SettlementScope? Scope { get; private set; }

    /// <summary>Settlement: who pays. Loan: the lender.</summary>
    public Guid? FromPersonId { get; private set; }

    /// <summary>Settlement: who receives. Loan: the borrower.</summary>
    public Guid? ToPersonId { get; private set; }

    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Who advanced money (split) — a loan or settlement has a single payer.</summary>
    public IReadOnlyCollection<OperationPayer> Payers => _payers;

    /// <summary>What each participant consumed (split only).</summary>
    public IReadOnlyCollection<OperationShare> Shares => _shares;

    public static DebtOperation Loan(
        Guid initiatorUserId,
        Guid borrowerPersonId,
        Guid walletId,
        decimal amount,
        Currency currency,
        DateTimeOffset occurredAt,
        string? description)
    {
        Money.EnsurePositive(amount);

        if (borrowerPersonId == initiatorUserId)
            throw new DomainException("loan.self", "You cannot lend money to yourself.");

        var operation = New(OperationKind.Loan, initiatorUserId, null, currency, amount, occurredAt, description);
        operation.FromPersonId = initiatorUserId;
        operation.ToPersonId = borrowerPersonId;
        operation._payers.Add(new OperationPayer(initiatorUserId, amount, walletId));

        return operation;
    }

    public static DebtOperation Split(
        Guid initiatorUserId,
        Guid? groupId,
        decimal total,
        Currency currency,
        SplitMethod method,
        Guid? categoryId,
        IReadOnlyList<OperationPayer> payers,
        IReadOnlyList<(Guid PersonId, decimal Amount)> shares,
        DateTimeOffset occurredAt,
        string? description)
    {
        Money.EnsurePositive(total, "total");

        if (payers.Count == 0)
            throw new DomainException("split.no_payers", "Somebody has to pay.");

        if (payers.Select(p => p.PersonId).Distinct().Count() != payers.Count)
            throw new DomainException("split.duplicate_payer", "Each payer can appear only once.");

        foreach (var payer in payers)
            Money.EnsurePositive(payer.Amount, "paid amount");

        if (payers.Sum(p => p.Amount) != total)
            throw new DomainException("split.payers_total", "What the payers paid must add up to the total.");

        if (shares.Sum(s => s.Amount) != total)
            throw new DomainException("split.shares_total", "The shares must add up to the total.");

        var operation = New(OperationKind.Split, initiatorUserId, groupId, currency, total, occurredAt, description);
        operation.Method = method;
        operation.CategoryId = categoryId;
        operation._payers.AddRange(payers);
        operation._shares.AddRange(shares.Where(s => s.Amount > 0).Select(s => new OperationShare(s.PersonId, s.Amount)));

        return operation;
    }

    public static DebtOperation Settlement(
        Guid initiatorUserId,
        SettlementScope scope,
        Guid? groupId,
        Guid fromPersonId,
        Guid toPersonId,
        decimal amount,
        Currency currency,
        Guid? walletId,
        DateTimeOffset occurredAt,
        string? description)
    {
        if (amount < 0)
            throw new DomainException("settlement.negative", "The settled amount cannot be negative.");

        var operation = New(OperationKind.Settlement, initiatorUserId, groupId, currency, amount, occurredAt, description);
        operation.Scope = scope;
        operation.FromPersonId = fromPersonId;
        operation.ToPersonId = toPersonId;

        if (amount > 0)
            operation._payers.Add(new OperationPayer(fromPersonId, amount, fromPersonId == initiatorUserId ? walletId : null));

        return operation;
    }

    public void Complete()
    {
        if (Status == OperationStatus.Completed)
            return;

        EnsurePending();
        Status = OperationStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Reject(string reason)
    {
        if (Status == OperationStatus.Rejected)
            return;

        EnsurePending();
        Status = OperationStatus.Rejected;
        RejectionReason = reason;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    private void EnsurePending()
    {
        if (Status != OperationStatus.Pending)
            throw new DomainException("operation.not_pending", $"The operation is already {Status}.");
    }

    private static DebtOperation New(
        OperationKind kind,
        Guid initiatorUserId,
        Guid? groupId,
        Currency currency,
        decimal total,
        DateTimeOffset occurredAt,
        string? description)
    {
        if (description?.Length > DescriptionMaxLength)
            throw new DomainException(
                "operation.description_too_long",
                $"Description cannot be longer than {DescriptionMaxLength} characters.");

        return new DebtOperation
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Status = OperationStatus.Pending,
            InitiatorUserId = initiatorUserId,
            GroupId = groupId,
            Currency = currency,
            Total = total,
            OccurredAt = occurredAt,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}

/// <param name="WalletId">Only for the initiator: the wallet the money left.</param>
public sealed record OperationPayer(Guid PersonId, decimal Amount, Guid? WalletId);

public sealed record OperationShare(Guid PersonId, decimal Amount);
