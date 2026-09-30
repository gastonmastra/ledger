using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Wallets.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Wallets.Application;

public interface IWalletsDbContext
{
    DbSet<Wallet> Wallets { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IWalletsEventBus : IEventBus;
