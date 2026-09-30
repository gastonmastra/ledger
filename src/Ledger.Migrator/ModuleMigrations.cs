using Ledger.Modules.Accounts.Infrastructure.Persistence;
using Ledger.Modules.Contacts.Infrastructure.Persistence;
using Ledger.Modules.Debts.Infrastructure;
using Ledger.Modules.Groups.Infrastructure;
using Ledger.Modules.Identity.Infrastructure.Persistence;
using Ledger.Modules.Transactions.Infrastructure.Persistence;
using Ledger.Modules.Wallets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ledger.Migrator;

public static class ModuleMigrations
{
    /// <summary>The DbContext of every module. Order does not matter: modules share no tables.</summary>
    public static readonly Type[] DbContexts =
    [
        typeof(IdentityDbContext),
        typeof(AccountsDbContext),
        typeof(WalletsDbContext),
        typeof(TransactionsDbContext),
        typeof(ContactsDbContext),
        typeof(GroupsDbContext),
        typeof(DebtsDbContext)
    ];

    public static async Task ApplyAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        foreach (var contextType in DbContexts)
        {
            await using var scope = services.CreateAsyncScope();
            var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);

            var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            logger.LogInformation(
                "{Context}: {Count} pending migration(s) {Migrations}",
                contextType.Name, pending.Count, pending);

            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        logger.LogInformation("All module databases are up to date");
    }
}
