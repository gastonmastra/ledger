using Ledger.Migrator;
using Ledger.Modules.Accounts.Infrastructure;
using Ledger.Modules.Contacts.Infrastructure;
using Ledger.Modules.Debts.Infrastructure;
using Ledger.Modules.Groups.Infrastructure;
using Ledger.Modules.Identity.Infrastructure;
using Ledger.Modules.Transactions.Infrastructure;
using Ledger.Modules.Wallets.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Applies the pending EF Core migrations of every module, each in its own schema, then exits.
// It is also the startup project for `dotnet ef` design-time commands.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services
    .AddIdentityPersistence(builder.Configuration)
    .AddAccountsPersistence(builder.Configuration)
    .AddWalletsPersistence(builder.Configuration)
    .AddTransactionsPersistence(builder.Configuration)
    .AddContactsPersistence(builder.Configuration)
    .AddGroupsPersistence(builder.Configuration)
    .AddDebtsPersistence(builder.Configuration);

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Ledger.Migrator");

try
{
    await ModuleMigrations.ApplyAsync(host.Services, logger, CancellationToken.None);
    return 0;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Database migration failed");
    return 1;
}
