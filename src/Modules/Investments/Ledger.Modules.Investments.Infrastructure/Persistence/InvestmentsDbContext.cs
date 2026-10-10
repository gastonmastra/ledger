using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Investments.Application;
using Ledger.Modules.Investments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Investments.Infrastructure.Persistence;

public sealed class InvestmentsDbContext(DbContextOptions<InvestmentsDbContext> options)
    : DbContext(options), IInvestmentsDbContext
{
    public const string Schema = "investments";

    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<AssetPrice> Prices => Set<AssetPrice>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.UseUtcDateTimeOffsets();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InvestmentsDbContext).Assembly);
        modelBuilder.AddMessagingTables();
    }
}
