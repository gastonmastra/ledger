using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Investments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Investments.Application;

public interface IInvestmentsDbContext
{
    DbSet<Asset> Assets { get; }
    DbSet<Trade> Trades { get; }
    DbSet<AssetPrice> Prices { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IInvestmentsEventBus : IEventBus;
