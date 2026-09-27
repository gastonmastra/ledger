using Ledger.Modules.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Accounts.Infrastructure;

public sealed class AccountsDbContext(
    DbContextOptions<AccountsDbContext> options) 
    : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("accounts");

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AccountsDbContext).Assembly);
    }
}