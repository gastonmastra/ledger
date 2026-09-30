using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Wallets.Application;
using Ledger.Modules.Wallets.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Wallets.Infrastructure.Persistence;

public sealed class WalletsDbContext(DbContextOptions<WalletsDbContext> options)
    : DbContext(options), IWalletsDbContext
{
    public const string Schema = "wallets";

    public DbSet<Wallet> Wallets => Set<Wallet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletsDbContext).Assembly);
        modelBuilder.AddMessagingTables();
    }
}
