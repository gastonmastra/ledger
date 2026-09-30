using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.Modules.Debts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application;

/// <param name="UserId">Set when the person is a registered user (their books get updated too).</param>
public sealed record PersonInfo(Guid PersonId, string Name, Guid? UserId);

/// <summary>Resolves person ids to names and registered users, from the point of view of one user.</summary>
internal sealed class PeopleDirectory(IDebtsDbContext dbContext)
{
    public async Task<PersonInfo> MeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new NotYetAvailableException("Your user is still being set up. Try again in a moment.");

        return new PersonInfo(userId, user.DisplayName, userId);
    }

    public async Task<PersonInfo> ContactAsync(Guid userId, Guid contactId, CancellationToken cancellationToken)
    {
        var contact = await dbContext.Contacts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ContactId == contactId, cancellationToken);

        // A contact created a moment ago may not have reached this module yet: let the client retry.
        if (contact is null)
            throw new NotYetAvailableException($"Contact '{contactId}' is not available yet. Try again in a moment.");

        if (contact.OwnerId != userId)
            throw new NotFoundException("Contact", contactId);

        return new PersonInfo(contact.PersonId, contact.Name, contact.LinkedUserId);
    }

    /// <summary>Me and my contacts, by person id.</summary>
    public async Task<IReadOnlyDictionary<Guid, PersonInfo>> CircleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var me = await MeAsync(userId, cancellationToken);

        var contacts = await dbContext.Contacts.AsNoTracking()
            .Where(x => x.OwnerId == userId)
            .ToListAsync(cancellationToken);

        var people = contacts.ToDictionary(c => c.PersonId, c => new PersonInfo(c.PersonId, c.Name, c.LinkedUserId));
        people[me.PersonId] = me;

        return people;
    }

    /// <summary>Members of a group the user belongs to, by person id.</summary>
    public async Task<(string GroupName, IReadOnlyDictionary<Guid, PersonInfo> Members)> GroupAsync(
        Guid userId,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var members = await dbContext.GroupMembers.AsNoTracking()
            .Where(x => x.GroupId == groupId)
            .ToListAsync(cancellationToken);

        if (members.All(m => m.UserId != userId))
            throw new NotFoundException("Group", groupId);

        return (
            members[0].GroupName,
            members.ToDictionary(m => m.PersonId, m => new PersonInfo(m.PersonId, m.DisplayName, m.UserId)));
    }

    /// <summary>Best known names for display: my contacts, members of my groups and myself.</summary>
    public async Task<Dictionary<Guid, string>> NamesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();

        var myGroups = dbContext.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId);

        foreach (var member in await dbContext.GroupMembers.AsNoTracking()
                     .Where(m => myGroups.Contains(m.GroupId))
                     .ToListAsync(cancellationToken))
        {
            names[member.PersonId] = member.DisplayName;
            names[member.GroupId] = member.GroupName;
        }

        foreach (var contact in await dbContext.Contacts.AsNoTracking()
                     .Where(c => c.OwnerId == userId)
                     .ToListAsync(cancellationToken))
            names[contact.PersonId] = contact.Name;

        names[userId] = "Me";

        return names;
    }
}
