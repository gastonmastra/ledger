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

    /// <summary>The contact takes part in a debt, split or group, so its person id is frozen.</summary>
    public bool IsInUse { get; private set; }

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

    public void LinkTo(Guid userId, string email)
    {
        if (userId == OwnerId)
            throw new DomainException("contact.self", "You cannot link a contact to yourself.");

        if (LinkedUserId is not null)
            throw new DomainException("contact.already_linked", "The contact is already linked to a user.");

        if (IsInUse)
            throw new DomainException(
                "contact.in_use",
                "The contact already takes part in debts or groups; create a new contact linked to the user instead.");

        LinkedUserId = userId;
        PersonId = userId;
        Email = email;
    }

    public void MarkInUse() => IsInUse = true;

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
