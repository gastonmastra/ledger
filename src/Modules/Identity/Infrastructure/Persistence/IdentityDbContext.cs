namespace Ledger.Modules.Identity.Infrastructure.Persistence;

using Ledger.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class IdentityDbContext : DbContext
{
    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options) 
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(IdentityDbContext).Assembly
        );
    }
}