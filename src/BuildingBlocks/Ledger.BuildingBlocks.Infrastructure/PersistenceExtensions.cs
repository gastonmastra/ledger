using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.BuildingBlocks.Infrastructure;

public static class PersistenceExtensions
{
    public const string ConnectionStringName = "Database";

    /// <summary>
    /// Registers a module DbContext that lives in its own PostgreSQL schema and keeps its
    /// own migrations history table inside that schema.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string schema)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString(ConnectionStringName),
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    schema)));

        return services;
    }

    /// <summary>
    /// Runs <paramref name="work"/> inside a database transaction, reusing the ambient one when
    /// the caller (e.g. the MassTransit consumer outbox) already started it.
    /// </summary>
    public static async Task<T> InTransactionAsync<T>(
        this DbContext dbContext,
        Func<Task<T>> work,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
            return await work();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var result = await work();

        await transaction.CommitAsync(cancellationToken);

        return result;
    }
}
