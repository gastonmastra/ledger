using Ledger.Modules.Accounts.Application.System;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ledger.Modules.Accounts.Infrastructure;

/// <summary>
/// Gives every existing user the system accounts added after they registered (e.g. "Balance
/// adjustments"). Provisioning is idempotent, so it only creates what is missing.
/// </summary>
internal sealed class SystemAccountsBackfill(
    IServiceScopeFactory scopeFactory,
    ILogger<SystemAccountsBackfill> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            IReadOnlyList<Guid> owners;

            await using (var scope = scopeFactory.CreateAsyncScope())
                owners = await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new GetAccountOwnersQuery(), stoppingToken);

            foreach (var owner in owners)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new ProvisionUserAccountsCommand(owner), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not provision the missing system accounts");
        }
    }
}
