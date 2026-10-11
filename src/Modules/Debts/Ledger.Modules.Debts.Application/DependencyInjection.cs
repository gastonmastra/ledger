using Ledger.Modules.Debts.Application.Links;
using Ledger.Modules.Debts.Application.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Debts.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDebtsApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<PeopleDirectory>();
        services.AddScoped<GroupBalances>();
        services.AddScoped<OperationFactory>();
        services.AddScoped<PaymentBook>();
        services.AddScoped<BooksLockGuard>();
        services.AddScoped<OperationLifecycle>();
        services.AddScoped<MergeGuard>();

        return services;
    }
}
