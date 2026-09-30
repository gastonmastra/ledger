using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Identity.Application;
using Ledger.Modules.Identity.Infrastructure.Persistence;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Identity.Infrastructure;

internal sealed class IdentityEventBus(IdentityDbContext dbContext)
    : ModuleEventBus<IdentityDbContext>(dbContext), IIdentityEventBus;

public static class IdentityModule
{
    public const string Name = "identity";

    public static IServiceCollection AddIdentityPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>(configuration, IdentityDbContext.Schema);
        services.AddScoped<IIdentityDbContext>(sp => sp.GetRequiredService<IdentityDbContext>());

        return services;
    }

    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIdentityPersistence(configuration);
        services.AddModuleApplication(typeof(IIdentityDbContext).Assembly);
        services.AddScoped<IIdentityEventBus, IdentityEventBus>();
        services.AddModuleMessaging<IdentityDbContext>(
            Name);

        return services;
    }
}
