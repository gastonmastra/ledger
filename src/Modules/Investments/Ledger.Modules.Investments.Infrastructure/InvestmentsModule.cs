using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Investments.Application;
using Ledger.Modules.Investments.Infrastructure.Persistence;
using Ledger.Modules.Transactions.Contracts;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Investments.Infrastructure;

internal sealed class InvestmentsEventBus(InvestmentsDbContext dbContext)
    : ModuleEventBus<InvestmentsDbContext>(dbContext), IInvestmentsEventBus;

internal sealed class InvestmentPostingCompletedConsumer(ISender sender, InvestmentsDbContext dbContext)
    : ModuleConsumer<InvestmentPostingCompleted, InvestmentsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<InvestmentPostingCompleted> context)
        => sender.Send(
            new CompleteTradeCommand(context.Message.TradeId, context.Message.WalletCurrency),
            context.CancellationToken);
}

internal sealed class InvestmentPostingRejectedConsumer(ISender sender, InvestmentsDbContext dbContext)
    : ModuleConsumer<InvestmentPostingRejected, InvestmentsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<InvestmentPostingRejected> context)
        => sender.Send(
            new RejectTradeCommand(context.Message.TradeId, context.Message.Reason),
            context.CancellationToken);
}

public static class InvestmentsModule
{
    public const string Name = "investments";

    public static IServiceCollection AddInvestmentsPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDbContext<InvestmentsDbContext>(configuration, InvestmentsDbContext.Schema);
        services.AddScoped<IInvestmentsDbContext>(sp => sp.GetRequiredService<InvestmentsDbContext>());

        return services;
    }

    public static IServiceCollection AddInvestmentsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddInvestmentsPersistence(configuration);
        services.AddModuleApplication(typeof(IInvestmentsDbContext).Assembly);
        services.AddScoped<IInvestmentsEventBus, InvestmentsEventBus>();
        services.AddModuleMessaging<InvestmentsDbContext>(
            Name,
            typeof(InvestmentPostingCompletedConsumer),
            typeof(InvestmentPostingRejectedConsumer));

        return services;
    }
}
