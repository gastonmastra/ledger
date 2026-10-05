namespace Ledger.Modules.Debts.Domain;

public enum PersonMergeStatus
{
    /// <summary>The copies are being recorded in the user's books; operations with the person are refused meanwhile.</summary>
    Pending,
    Applied,
    Failed
}

/// <summary>
/// A contact's person becomes a registered user (the user accepted a link request): the shared history is copied
/// into the user's books, then every debt, operation and group member of the person is moved to the user.
/// </summary>
public sealed class PersonMerge
{
    public const int ReasonMaxLength = 1000;

    private PersonMerge()
    {
        Parties = null!;
    }

    public Guid Id { get; private set; }

    /// <summary>The link request in the Contacts module.</summary>
    public Guid RequestId { get; private set; }

    public Guid PreviousPersonId { get; private set; }
    public Guid UserId { get; private set; }
    public PersonMergeStatus Status { get; private set; }

    /// <summary>People the user will share debts with (JSON), announced when it is applied.</summary>
    public string Parties { get; private set; }

    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static PersonMerge Start(Guid requestId, Guid previousPersonId, Guid userId, string parties)
        => new()
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            PreviousPersonId = previousPersonId,
            UserId = userId,
            Status = PersonMergeStatus.Pending,
            Parties = parties,
            CreatedAt = DateTimeOffset.UtcNow
        };

    /// <returns>False when it was already finished (duplicate answer).</returns>
    public bool MarkApplied()
    {
        if (Status != PersonMergeStatus.Pending)
            return false;

        Status = PersonMergeStatus.Applied;
        CompletedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <returns>False when it was already finished (duplicate answer).</returns>
    public bool MarkFailed(string reason)
    {
        if (Status != PersonMergeStatus.Pending)
            return false;

        Status = PersonMergeStatus.Failed;
        FailureReason = reason.Length > ReasonMaxLength ? reason[..ReasonMaxLength] : reason;
        CompletedAt = DateTimeOffset.UtcNow;
        return true;
    }
}

/// <summary>Local copy of an open link request: who may see the preview of what linking copies.</summary>
public sealed class DebtLinkRequest
{
    private DebtLinkRequest()
    {
    }

    public Guid RequestId { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid TargetUserId { get; private set; }

    public static DebtLinkRequest Create(Guid requestId, Guid ownerId, Guid personId, Guid targetUserId)
        => new() { RequestId = requestId, OwnerId = ownerId, PersonId = personId, TargetUserId = targetUserId };
}
