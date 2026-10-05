using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Transactions.Application;
using Ledger.Modules.Transactions.Infrastructure.Consumers;
using Ledger.Modules.Transactions.Infrastructure.Persistence;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Transactions.Infrastructure;

internal sealed class TransactionsEventBus(TransactionsDbContext dbContext)
    : ModuleEventBus<TransactionsDbContext>(dbContext), ITransactionsEventBus;

public static class TransactionsModule
{
    public const string Name = "transactions";

    public static IServiceCollection AddTransactionsPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDbContext<TransactionsDbContext>(configuration, TransactionsDbContext.Schema);
        services.AddScoped<ITransactionsDbContext>(sp => sp.GetRequiredService<TransactionsDbContext>());

        return services;
    }

    public static IServiceCollection AddTransactionsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTransactionsPersistence(configuration);
        services.AddModuleApplication(typeof(ITransactionsDbContext).Assembly);
        services.AddTransactionsApplicationServices();
        services.AddScoped<ITransactionsEventBus, TransactionsEventBus>();
        services.AddHostedService<ExistingDebtsMigrationRunner>();
        services.AddModuleMessaging<TransactionsDbContext>(
            Name,
            typeof(AccountCreatedConsumer),
            typeof(AccountRenamedConsumer),
            typeof(AccountArchivedConsumer),
            typeof(AccountDeletedConsumer),
            typeof(ContactCreatedConsumer),
            typeof(ContactLinkedConsumer),
            typeof(GroupCreatedConsumer),
            typeof(GroupMemberAddedConsumer),
            typeof(LedgerPostingRequestedConsumer),
            typeof(LedgerRevisionRequestedConsumer),
            typeof(InvestmentPostingRequestedConsumer),
            typeof(LedgerOpeningsRequestedConsumer));

        return services;
    }
}
