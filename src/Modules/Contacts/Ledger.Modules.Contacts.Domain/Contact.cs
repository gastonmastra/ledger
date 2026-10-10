using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Contacts.Domain;

public enum ContactOrigin
{
    /// <summary>Created by the owner.</summary>
    Manual,
    /// <summary>Created automatically because somebody else recorded a debt or group with the owner.</summary>
    Mirrored
}

/// <summary>A person the user lends money to or shares expenses with.</summary>
public sealed class Contact
{
    public const int NameMaxLength = 200;

    private Contact()
    {
        Name = null!;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }

    /// <summary>
    /// Global id of the person, shared by every module and every user: the user id when the
    /// contact is a registered user, otherwise an id minted for this person.
    /// </summary>
    public Guid PersonId { get; private set; }

    public string Name { get; private set; }
    public string? Email { get; private set; }
    public Guid? LinkedUserId { get; private set; }
    public ContactOrigin Origin { get; private set; }

    /// <summary>
    /// The contact takes part in a debt, split or group: linking it to a user needs that user's approval
    /// (a <see cref="ContactLinkRequest"/>), because the shared history is copied into their books.
    /// </summary>
    public bool IsInUse { get; private set; }

    /// <summary>A link request waiting for the user's answer (or being applied).</summary>
    public Guid? PendingLinkRequestId { get; private set; }

    /// <summary>
    /// Set when this contact was merged into another contact of the same owner (both were the same person).
    /// A merged contact is hidden; its previous person id stays here only as history.
    /// </summary>
    public Guid? MergedIntoContactId { get; private set; }

    public bool IsMerged => MergedIntoContactId is not null;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Contact Create(Guid ownerId, string name, string? email, Guid? linkedUserId)
    {
        if (linkedUserId == ownerId)
            throw new DomainException("contact.self", "You cannot add yourself as a contact.");

        var id = Guid.NewGuid();

        return new Contact
        {
            Id = id,
            OwnerId = ownerId,
            PersonId = linkedUserId ?? id,
            Name = ValidateName(name),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            LinkedUserId = linkedUserId,
            Origin = ContactOrigin.Manual,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static Contact CreateMirrored(Guid ownerId, Guid personId, string name, Guid? linkedUserId)
        => new()
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            PersonId = personId,
            Name = ValidateName(name),
            LinkedUserId = linkedUserId,
            Origin = ContactOrigin.Mirrored,
            IsInUse = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

    /// <summary>Links a contact that is not used yet: nothing to copy, so it happens right away.</summary>
    public void LinkTo(Guid userId, string email)
    {
        EnsureCanLink(userId);

        if (IsInUse)
            throw new DomainException(
                "contact.in_use",
                "The contact already takes part in debts or groups: the user has to accept the link.");

        LinkedUserId = userId;
        PersonId = userId;
        Email = email;
    }

    /// <summary>Starts linking a contact that is in use: the user has to accept the request first.</summary>
    public ContactLinkRequest RequestLink(Guid userId)
    {
        EnsureCanLink(userId);

        if (!IsInUse)
            throw new DomainException("contact.not_in_use", "The contact is not used yet: link it directly.");

        if (PendingLinkRequestId is not null)
            throw new DomainException("contact.link_pending", "A link request for this contact is waiting for an answer.");

        var request = ContactLinkRequest.Create(this, userId);
        PendingLinkRequestId = request.Id;

        return request;
    }

    /// <summary>The person was merged into the user (the request was accepted and applied): the contact becomes the user.</summary>
    public void CompleteMerge(Guid userId, string? email)
    {
        PendingLinkRequestId = null;
        LinkedUserId = userId;
        PersonId = userId;
        Email = email ?? Email;
        IsInUse = true;
    }

    /// <summary>
    /// The person was merged into a user the owner already had as another contact: this contact folds into that one.
    /// Its person id is kept (it is unique per owner) as history.
    /// </summary>
    public void MergeInto(Contact survivor)
    {
        PendingLinkRequestId = null;
        MergedIntoContactId = survivor.Id;
        LinkedUserId = survivor.LinkedUserId;
        survivor.MarkInUse();
    }

    public void ClearLinkRequest(Guid requestId)
    {
        if (PendingLinkRequestId == requestId)
            PendingLinkRequestId = null;
    }

    public void MarkInUse() => IsInUse = true;

    private void EnsureCanLink(Guid userId)
    {
        if (userId == OwnerId)
            throw new DomainException("contact.self", "You cannot link a contact to yourself.");

        if (IsMerged)
            throw new DomainException("contact.merged", "The contact was merged into another contact.");

        if (LinkedUserId is not null)
            throw new DomainException("contact.already_linked", "The contact is already linked to a user.");
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("contact.name_required", "Contact name is required.");

        name = name.Trim();

        return name.Length > NameMaxLength ? name[..NameMaxLength] : name;
    }
}

/// <summary>Local copy of the registered users, used to link contacts by email.</summary>
public sealed class RegisteredUser
{
    private RegisteredUser()
    {
        Email = null!;
        DisplayName = null!;
    }

    public Guid UserId { get; private set; }
    public string Email { get; private set; }
    public string DisplayName { get; private set; }

    public static RegisteredUser Create(Guid userId, string email, string displayName)
        => new() { UserId = userId, Email = email.ToLowerInvariant(), DisplayName = displayName };
}
