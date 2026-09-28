using Ledger.Modules.Accounts.Application;
using Ledger.Modules.Accounts.Application.CreateAccount;
using Ledger.Modules.Accounts.Application.GetAccount;
using Ledger.Modules.Accounts.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Accounts;

public static class AccountsModule
{
    public static IServiceCollection AddAccountsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AccountsDbContext>(options =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("Database"),
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    "accounts"
                )
            );
        });

        services.AddScoped<IAccountReader, AccountReader>();
        services.AddScoped<GetAccountHandler>();
        services.AddScoped<CreateAccountHandler>();

        return services;
    }
}