using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Identity.Application;

public interface IIdentityDbContext
{
    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IIdentityEventBus : IEventBus;
