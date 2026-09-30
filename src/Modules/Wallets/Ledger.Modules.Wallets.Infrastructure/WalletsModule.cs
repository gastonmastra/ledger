using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Transactions.Contracts;
using Ledger.Modules.Wallets.Application;
using Ledger.Modules.Wallets.Infrastructure.Persistence;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Wallets.Infrastructure;

internal sealed class WalletsEventBus(WalletsDbContext dbContext)
    : ModuleEventBus<WalletsDbContext>(dbContext), IWalletsEventBus;

internal sealed class WalletBalanceChangedConsumer(ISender sender, WalletsDbContext dbContext)
    : ModuleConsumer<WalletBalanceChanged, WalletsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<WalletBalanceChanged> context)
    {
        var message = context.Message;

        return sender.Send(
            new ApplyWalletBalanceCommand(message.WalletId, message.Balance, message.TransactionCount, message.Version),
            context.CancellationToken);
    }
}

public static class WalletsModule
{
    public const string Name = "wallets";

    public static IServiceCollection AddWalletsPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDbContext<WalletsDbContext>(configuration, WalletsDbContext.Schema);
        services.AddScoped<IWalletsDbContext>(sp => sp.GetRequiredService<WalletsDbContext>());

        return services;
    }

    public static IServiceCollection AddWalletsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddWalletsPersistence(configuration);
        services.AddModuleApplication(typeof(IWalletsDbContext).Assembly);
        services.AddScoped<IWalletsEventBus, WalletsEventBus>();
        services.AddModuleMessaging<WalletsDbContext>(
            Name,
            typeof(WalletBalanceChangedConsumer));

        return services;
    }
}
