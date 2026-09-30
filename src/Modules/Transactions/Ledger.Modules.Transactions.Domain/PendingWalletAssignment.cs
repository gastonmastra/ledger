using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Transactions.Domain;

public enum PendingWalletAssignmentStatus
{
    Pending,
    Assigned
}

/// <summary>
/// Somebody else recorded money that the user paid or received (e.g. a friend recorded a loan
/// to the user). The money waits in the pending settlements account until the user tells which
/// wallet it actually came from or went to.
/// </summary>
public sealed class PendingWalletAssignment
{
    private PendingWalletAssignment()
    {
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid OperationId { get; private set; }

    /// <summary>Positive: money the user received. Negative: money the user paid.</summary>
    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }
    public Guid? CounterpartyId { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public PendingWalletAssignmentStatus Status { get; private set; }
    public Guid? WalletId { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }

    public static PendingWalletAssignment Create(
        Guid ownerId,
        Guid operationId,
        decimal amount,
        Currency currency,
        Guid? counterpartyId,
        string? description,
        DateTimeOffset occurredAt)
        => new()
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            OperationId = operationId,
            Amount = amount,
            Currency = currency,
            CounterpartyId = counterpartyId,
            Description = description,
            OccurredAt = occurredAt,
            Status = PendingWalletAssignmentStatus.Pending
        };

    public void Assign(Guid walletId)
    {
        if (Status == PendingWalletAssignmentStatus.Assigned)
            throw new DomainException("assignment.already_assigned", "The wallet was already assigned.");

        Status = PendingWalletAssignmentStatus.Assigned;
        WalletId = walletId;
        AssignedAt = DateTimeOffset.UtcNow;
    }
}
