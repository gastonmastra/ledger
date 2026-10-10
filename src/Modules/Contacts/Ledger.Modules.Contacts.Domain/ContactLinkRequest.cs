using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Contacts.Domain;

public enum LinkRequestStatus
{
    /// <summary>Waiting for the user's answer.</summary>
    Pending,
    /// <summary>Accepted: the person is being merged and the history copied into the user's books.</summary>
    Applying,
    Applied,
    Rejected,
    Cancelled,
    /// <summary>Accepted, but it could not be applied (e.g. the user's books are closed for those dates).</summary>
    Failed
}

/// <summary>
/// The owner wants a contact that already takes part in debts or groups to become a registered user. Accepting it
/// merges that person into the user everywhere and copies the shared history into the user's books.
/// </summary>
public sealed class ContactLinkRequest
{
    public const int ReasonMaxLength = 1000;

    private ContactLinkRequest()
    {
        ContactName = null!;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid ContactId { get; private set; }

    /// <summary>The contact's name when the request was made (what the user sees).</summary>
    public string ContactName { get; private set; }

    /// <summary>The person id being merged.</summary>
    public Guid PersonId { get; private set; }

    public Guid TargetUserId { get; private set; }
    public LinkRequestStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AnsweredAt { get; private set; }

    public bool IsOpen => Status is LinkRequestStatus.Pending or LinkRequestStatus.Applying;

    internal static ContactLinkRequest Create(Contact contact, Guid targetUserId)
        => new()
        {
            Id = Guid.NewGuid(),
            OwnerId = contact.OwnerId,
            ContactId = contact.Id,
            ContactName = contact.Name,
            PersonId = contact.PersonId,
            TargetUserId = targetUserId,
            Status = LinkRequestStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

    public void Accept(Guid userId)
    {
        EnsureTarget(userId);
        EnsurePending();

        Status = LinkRequestStatus.Applying;
        AnsweredAt = DateTimeOffset.UtcNow;
    }

    public void Reject(Guid userId)
    {
        EnsureTarget(userId);
        EnsurePending();

        Status = LinkRequestStatus.Rejected;
        AnsweredAt = DateTimeOffset.UtcNow;
    }

    public void Cancel(Guid ownerId)
    {
        if (ownerId != OwnerId)
            throw new DomainException("link.not_yours", "Only who sent the link request can cancel it.");

        EnsurePending();
        Status = LinkRequestStatus.Cancelled;
        AnsweredAt = DateTimeOffset.UtcNow;
    }

    /// <returns>False when it was already finished (duplicate message).</returns>
    public bool MarkApplied()
    {
        if (Status != LinkRequestStatus.Applying)
            return false;

        Status = LinkRequestStatus.Applied;
        return true;
    }

    /// <returns>False when it was already finished (duplicate message).</returns>
    public bool MarkFailed(string reason)
    {
        if (Status != LinkRequestStatus.Applying)
            return false;

        Status = LinkRequestStatus.Failed;
        FailureReason = reason.Length > ReasonMaxLength ? reason[..ReasonMaxLength] : reason;
        return true;
    }

    private void EnsureTarget(Guid userId)
    {
        if (userId != TargetUserId)
            throw new DomainException("link.not_for_you", "This link request is for another user.");
    }

    private void EnsurePending()
    {
        if (Status != LinkRequestStatus.Pending)
            throw new DomainException("link.answered", "This link request was already answered.");
    }
}
