using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ledger.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Publishes the committed outbox messages of one module, in order. Rows are claimed with
/// <c>FOR UPDATE SKIP LOCKED</c>, so several instances of the module can run side by side.
/// </summary>
public sealed class OutboxProcessor<TDbContext>(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessor<TDbContext>> logger)
    : BackgroundService
    where TDbContext : DbContext
{
    private const int BatchSize = 50;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int published;

            try
            {
                published = await PublishBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox of {Module} failed", typeof(TDbContext).Name);
                published = 0;
            }

            if (published < BatchSize)
                await Task.Delay(IdleDelay, stoppingToken);
        }
    }

    private async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var entityType = dbContext.Model.FindEntityType(typeof(OutboxMessage))!;

        // Identifiers come from the EF model, never from user input.
        var sql =
            $"SELECT * FROM \"{entityType.GetSchema()}\".\"{entityType.GetTableName()}\" " +
            $"WHERE \"ProcessedAt\" IS NULL ORDER BY \"OccurredAt\" LIMIT {BatchSize} FOR UPDATE SKIP LOCKED";

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var messages = await dbContext.Set<OutboxMessage>()
            .FromSqlRaw(sql)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                var type = Type.GetType(message.Type)
                    ?? throw new InvalidOperationException($"Unknown message type '{message.Type}'.");
                var payload = JsonSerializer.Deserialize(message.Content, type, OutboxSerializer.Options)!;

                await publisher.Publish(payload, type, context => context.MessageId = message.Id, cancellationToken);

                message.MarkProcessed();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not publish outbox message {MessageId}", message.Id);
                message.MarkFailed(ex.Message);

                // Keep the order: later messages wait until this one is published.
                break;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return messages.Count;
    }
}
