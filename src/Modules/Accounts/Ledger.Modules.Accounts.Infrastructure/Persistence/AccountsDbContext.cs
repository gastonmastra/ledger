using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Accounts.Application;
using Ledger.Modules.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Accounts.Infrastructure.Persistence;

public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options)
    : DbContext(options), IAccountsDbContext
{
    public const string Schema = "accounts";

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccountsDbContext).Assembly);
        modelBuilder.AddMessagingTables();
    }
}
