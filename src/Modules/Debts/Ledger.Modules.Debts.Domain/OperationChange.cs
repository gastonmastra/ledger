using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Debts.Domain;

/// <summary>
/// A request to edit, delete or restore a shared operation. Linked users involved (other than the
/// author) must accept or reject it; each one decides for their own books. When everybody has
/// answered, the change is applied to the author and to those who accepted; if anybody rejected,
/// the operation is marked as disputed.
/// </summary>
public sealed class OperationChange
{
    public const int ReasonMaxLength = 500;

    private readonly List<ChangeApproval> _approvals = [];

    private OperationChange()
    {
        Reason = null!;
    }

    public Guid Id { get; private set; }

    /// <summary>The version being changed.</summary>
    public Guid OperationId { get; private set; }

    public ChangeKind Kind { get; private set; }
    public ChangeStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string Reason { get; private set; }

    /// <summary>The new version as it was requested (JSON), null for a delete.</summary>
    public string? Draft { get; private set; }

    /// <summary>What the new version looks like (JSON), null for a delete.</summary>
    public string? Preview { get; private set; }

    /// <summary>The new version, once created (edit and restore).</summary>
    public Guid? NewOperationId { get; private set; }

    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }

    public IReadOnlyCollection<ChangeApproval> Approvals => _approvals;

    public bool AllAnswered => _approvals.All(a => a.Status != ApprovalStatus.Pending);

    public IEnumerable<Guid> AcceptedUserIds => _approvals.Where(a => a.Status == ApprovalStatus.Accepted).Select(a => a.UserId);

    public bool AnyRejected => _approvals.Any(a => a.Status == ApprovalStatus.Rejected);

    public static OperationChange Request(
        Guid operationId,
        ChangeKind kind,
        Guid requestedBy,
        string reason,
        string? draft,
        string? preview,
        IEnumerable<Guid> approvers)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("change.reason_required", "Explain why the operation changes.");

        reason = reason.Trim();

        if (reason.Length > ReasonMaxLength)
            throw new DomainException("change.reason_too_long", $"The reason cannot be longer than {ReasonMaxLength} characters.");

        var change = new OperationChange
        {
            Id = Guid.NewGuid(),
            OperationId = operationId,
            Kind = kind,
            Status = ChangeStatus.Pending,
            RequestedByUserId = requestedBy,
            Reason = reason,
            Draft = draft,
            Preview = preview,
            CreatedAt = DateTimeOffset.UtcNow
        };

        foreach (var userId in approvers.Where(u => u != requestedBy).Distinct())
            change._approvals.Add(new ChangeApproval(change.Id, userId));

        return change;
    }

    public void Respond(Guid userId, bool accept, string? reason)
    {
        if (Status != ChangeStatus.Pending)
            throw new DomainException("change.not_pending", "This change is no longer waiting for an answer.");

        var approval = _approvals.SingleOrDefault(a => a.UserId == userId)
            ?? throw new DomainException("change.not_approver", "This change does not need your confirmation.");

        approval.Respond(accept, reason);
    }

    public void StartApplying(Guid? newOperationId)
    {
        Status = ChangeStatus.Applying;
        NewOperationId = newOperationId;
    }

    public void MarkApplied()
    {
        Status = ChangeStatus.Applied;
        AppliedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status = ChangeStatus.Failed;
        FailureReason = reason.Length > 1000 ? reason[..1000] : reason;
        AppliedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class ChangeApproval
{
    private ChangeApproval()
    {
    }

    internal ChangeApproval(Guid changeId, Guid userId)
    {
        ChangeId = changeId;
        UserId = userId;
        Status = ApprovalStatus.Pending;
    }

    public Guid ChangeId { get; private set; }
    public Guid UserId { get; private set; }
    public ApprovalStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }

    internal void Respond(bool accept, string? reason)
    {
        if (Status != ApprovalStatus.Pending)
            throw new DomainException("change.already_answered", "You already answered this change.");

        Status = accept ? ApprovalStatus.Accepted : ApprovalStatus.Rejected;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        RespondedAt = DateTimeOffset.UtcNow;
    }
}
