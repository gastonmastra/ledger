using Ledger.Modules.Transactions.Application.Ledger;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ledger.Modules.Transactions.Infrastructure;

/// <summary>Runs the one-off "existing debts are not income/expenses" migration at startup (idempotent).</summary>
internal sealed class ExistingDebtsMigrationRunner(
    IServiceScopeFactory scopeFactory,
    ILogger<ExistingDebtsMigrationRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var migrated = await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new MigrateExistingDebtsCommand(), stoppingToken);

            if (migrated > 0)
                logger.LogInformation("Moved {Count} existing debts to opening balances", migrated);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not migrate existing debts to opening balances");
        }
    }
}
