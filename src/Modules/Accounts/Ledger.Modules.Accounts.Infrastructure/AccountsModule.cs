using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Accounts.Application;
using Ledger.Modules.Accounts.Infrastructure.Consumers;
using Ledger.Modules.Accounts.Infrastructure.Persistence;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Accounts.Infrastructure;

internal sealed class AccountsEventBus(AccountsDbContext dbContext)
    : ModuleEventBus<AccountsDbContext>(dbContext), IAccountsEventBus;

public static class AccountsModule
{
    public const string Name = "accounts";

    public static IServiceCollection AddAccountsPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDbContext<AccountsDbContext>(configuration, AccountsDbContext.Schema);
        services.AddScoped<IAccountsDbContext>(sp => sp.GetRequiredService<AccountsDbContext>());

        return services;
    }

    public static IServiceCollection AddAccountsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAccountsPersistence(configuration);
        services.AddModuleApplication(typeof(IAccountsDbContext).Assembly);
        services.AddScoped<IAccountsEventBus, AccountsEventBus>();
        services.AddHostedService<SystemAccountsBackfill>();
        services.AddModuleMessaging<AccountsDbContext>(
            Name,
            typeof(UserRegisteredConsumer),
            typeof(WalletCreatedConsumer),
            typeof(WalletRenamedConsumer),
            typeof(WalletArchivedConsumer),
            typeof(WalletDeletedConsumer));

        return services;
    }
}
