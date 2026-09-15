using Ledger.Modules.Transactions.Application.CreateTransaction;
using Ledger.Modules.Transactions.Application.GetTransaction;
using Ledger.Modules.Transactions.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Transactions;

public static class TransactionsModule
{
    public static IServiceCollection AddTransactionsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<TransactionsDbContext>(options => 
            options.UseNpgsql(
                configuration.GetConnectionString("Database"),
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    "transactions"
                )
            )
        );

        services.AddScoped<CreateTransactionHandler>();
        services.AddScoped<GetTransactionHandler>();

        return services;
    }
}