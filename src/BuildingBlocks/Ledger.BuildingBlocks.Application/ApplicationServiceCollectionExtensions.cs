using System.Reflection;
using FluentValidation;
using Ledger.BuildingBlocks.Application.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.BuildingBlocks.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers MediatR handlers and FluentValidation validators of the given module
    /// application assemblies, plus the shared pipeline behaviors (once).
    /// </summary>
    public static IServiceCollection AddModuleApplication(
        this IServiceCollection services,
        Assembly applicationAssembly)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(applicationAssembly));
        services.AddValidatorsFromAssembly(applicationAssembly, includeInternalTypes: true);

        if (services.All(d => d.ImplementationType != typeof(ValidationBehavior<,>)))
        {
            services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        }

        return services;
    }
}
