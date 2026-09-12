using Ledger.Modules.Wallets.Application.CreateWallet;
using Ledger.Modules.Wallets.Application.GetWallets;
using Ledger.Modules.Wallets.Domain;
using Ledger.Modules.Wallets.Infrastructure;
using Ledger.Modules.Wallets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Wallets;

public static class WalletsModule
{
    public static IServiceCollection AddWalletsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<WalletsDbContext>(options => 
            options.UseNpgsql(
                configuration.GetConnectionString("Database"),
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    "wallets"
                )
            )
        );

        services.AddScoped<IWalletRepository, WalletRepository>();

        services.AddScoped<GetWalletsHandler>();
        services.AddScoped<CreateWalletHandler>();

        return services;
    }
}