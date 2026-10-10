using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Groups.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Groups.Application;

public interface IGroupsDbContext
{
    DbSet<Group> Groups { get; }
    DbSet<KnownContact> KnownContacts { get; }
    DbSet<KnownUser> KnownUsers { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// A member without an account turned out to be a registered user: every membership of
    /// <paramref name="previousPersonId"/> becomes the user (member keys include the person id, so it is a SQL update).
    /// </summary>
    Task ReplaceMemberAsync(Guid previousPersonId, Guid userId, string displayName, CancellationToken cancellationToken);
}

public interface IGroupsEventBus : IEventBus;
