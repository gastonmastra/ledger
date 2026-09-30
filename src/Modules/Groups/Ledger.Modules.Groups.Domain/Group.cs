using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Groups.Domain;

/// <summary>People who share expenses (a trip, a flat...). Shared with every member that is a registered user.</summary>
public sealed class Group
{
    public const int NameMaxLength = 100;

    private readonly List<GroupMember> _members = [];

    private Group()
    {
        Name = null!;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<GroupMember> Members => _members;

    public static Group Create(string name, Guid creatorUserId, string creatorName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("group.name_required", "Group name is required.");

        name = name.Trim();

        if (name.Length > NameMaxLength)
            throw new DomainException("group.name_too_long", $"Group name cannot be longer than {NameMaxLength} characters.");

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedBy = creatorUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // A registered user's person id is its user id.
        group._members.Add(GroupMember.Create(group.Id, creatorUserId, creatorName, creatorUserId));

        return group;
    }

    public bool IsVisibleTo(Guid userId) => _members.Any(m => m.UserId == userId);

    public GroupMember AddMember(Guid personId, string displayName, Guid? userId)
    {
        if (_members.Any(m => m.PersonId == personId))
            throw new DomainException("group.already_member", $"{displayName} is already a member of the group.");

        var member = GroupMember.Create(Id, personId, displayName, userId);
        _members.Add(member);

        return member;
    }

    public void RemoveMember(Guid personId)
    {
        var member = _members.SingleOrDefault(m => m.PersonId == personId)
            ?? throw new DomainException("group.not_member", "The person is not a member of the group.");

        if (member.UserId == CreatedBy)
            throw new DomainException("group.remove_creator", "The creator of the group cannot be removed.");

        if (member.HasActivity)
            throw new DomainException(
                "group.member_has_activity",
                $"{member.DisplayName} already took part in group expenses and cannot be removed.");

        _members.Remove(member);
    }

    public void MarkActivity(IEnumerable<Guid> personIds)
    {
        foreach (var member in _members.Where(m => personIds.Contains(m.PersonId)))
            member.MarkActivity();
    }
}

public sealed class GroupMember
{
    private GroupMember()
    {
        DisplayName = null!;
    }

    public Guid GroupId { get; private set; }
    public Guid PersonId { get; private set; }
    public string DisplayName { get; private set; }
    public Guid? UserId { get; private set; }
    public bool HasActivity { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    internal static GroupMember Create(Guid groupId, Guid personId, string displayName, Guid? userId)
        => new()
        {
            GroupId = groupId,
            PersonId = personId,
            DisplayName = displayName,
            UserId = userId,
            AddedAt = DateTimeOffset.UtcNow
        };

    internal void MarkActivity() => HasActivity = true;
}

/// <summary>Local copy of a contact of some user (to add it to groups).</summary>
public sealed class KnownContact
{
    private KnownContact()
    {
        Name = null!;
    }

    public Guid ContactId { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid PersonId { get; private set; }
    public string Name { get; private set; }
    public Guid? LinkedUserId { get; private set; }

    public static KnownContact Create(Guid contactId, Guid ownerId, Guid personId, string name, Guid? linkedUserId)
        => new()
        {
            ContactId = contactId,
            OwnerId = ownerId,
            PersonId = personId,
            Name = name,
            LinkedUserId = linkedUserId
        };

    public void Link(Guid personId, Guid userId)
    {
        PersonId = personId;
        LinkedUserId = userId;
    }
}

/// <summary>Local copy of the display name of registered users.</summary>
public sealed class KnownUser
{
    private KnownUser()
    {
        DisplayName = null!;
    }

    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; }

    public static KnownUser Create(Guid userId, string displayName)
        => new() { UserId = userId, DisplayName = displayName };
}
