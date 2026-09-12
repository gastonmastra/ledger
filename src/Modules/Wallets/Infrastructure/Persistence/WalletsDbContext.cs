using Ledger.Modules.Wallets.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Wallets.Infrastructure.Persistence;

public sealed class WalletsDbContext : DbContext
{
    public WalletsDbContext(
        DbContextOptions<WalletsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Wallet> Wallets => Set<Wallet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("wallets");
        
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(WalletsDbContext).Assembly
        );
    }
}