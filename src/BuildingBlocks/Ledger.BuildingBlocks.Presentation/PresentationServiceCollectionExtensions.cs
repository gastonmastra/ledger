using Ledger.BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.BuildingBlocks.Presentation;

public static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddPresentationCore(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddProblemDetails();
        services.AddExceptionHandler<ExceptionHandler>();

        return services;
    }
}
