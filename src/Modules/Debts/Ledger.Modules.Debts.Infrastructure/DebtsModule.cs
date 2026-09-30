using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Debts.Application;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Debts.Infrastructure;

internal sealed class DebtsEventBus(DebtsDbContext dbContext)
    : ModuleEventBus<DebtsDbContext>(dbContext), IDebtsEventBus;

public static class DebtsModule
{
    public const string Name = "debts";

    public static IServiceCollection AddDebtsPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDbContext<DebtsDbContext>(configuration, DebtsDbContext.Schema);
        services.AddScoped<IDebtsDbContext>(sp => sp.GetRequiredService<DebtsDbContext>());

        return services;
    }

    public static IServiceCollection AddDebtsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDebtsPersistence(configuration);
        services.AddModuleApplication(typeof(IDebtsDbContext).Assembly);
        services.AddDebtsApplicationServices();
        services.AddScoped<IDebtsEventBus, DebtsEventBus>();
        services.AddModuleMessaging<DebtsDbContext>(
            Name,
            typeof(LedgerPostingCompletedConsumer),
            typeof(LedgerPostingRejectedConsumer),
            typeof(LedgerRevisionCompletedConsumer),
            typeof(LedgerRevisionRejectedConsumer),
            typeof(BooksLockChangedConsumer),
            typeof(UserRegisteredConsumer),
            typeof(ContactCreatedConsumer),
            typeof(ContactLinkedConsumer),
            typeof(GroupCreatedConsumer),
            typeof(GroupMemberAddedConsumer),
            typeof(GroupMemberRemovedConsumer),
            typeof(WalletCreatedConsumer),
            typeof(WalletDeletedConsumer));

        return services;
    }
}
