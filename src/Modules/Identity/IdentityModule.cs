using Ledger.Modules.Identity.Application.Users.GetOrCreateUser;
using Ledger.Modules.Identity.Domain;
using Ledger.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Database"),
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    "identity"
                )
            )
        );

        services.AddScoped<IUserRepository, UserRepository>();

        services.AddScoped<GetOrCreateUserHandler>();
        
        return services;
    }
}