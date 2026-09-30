using Ledger.Modules.Transactions.Application.Ledger;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Transactions.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddTransactionsApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<AccountResolver>();
        services.AddScoped<LedgerWriter>();
        services.AddScoped<MovementFactory>();
        services.AddScoped<Queries.TransactionMapper>();

        return services;
    }
}
