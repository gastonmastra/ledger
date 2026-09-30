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
}

public interface IGroupsEventBus : IEventBus;
