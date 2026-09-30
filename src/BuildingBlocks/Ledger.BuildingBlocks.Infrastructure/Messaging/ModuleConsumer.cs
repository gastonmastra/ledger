using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Ledger.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Base class of every integration event consumer. The message is handled exactly once per
/// consumer: its id is recorded in the module inbox in the same database transaction as the
/// changes (and outbox messages) the handler produces. Redeliveries are skipped.
/// </summary>
public abstract class ModuleConsumer<TMessage, TDbContext>(TDbContext dbContext) : IConsumer<TMessage>
    where TMessage : class
    where TDbContext : DbContext
{
    public async Task Consume(ConsumeContext<TMessage> context)
    {
        var messageId = context.MessageId
            ?? throw new InvalidOperationException("Integration messages must have a message id.");
        var cancellationToken = context.CancellationToken;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await TryRecordAsync(messageId, cancellationToken))
                return; // Already handled: the transaction is rolled back on dispose.

            await Handle(context);

            // Flushes outbox messages published by the handler after its last save.
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Nothing was committed; do not leak tracked entities into the retry.
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    protected abstract Task Handle(ConsumeContext<TMessage> context);

    private async Task<bool> TryRecordAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(InboxMessage))
            ?? throw new InvalidOperationException($"{typeof(TDbContext).Name} has no inbox table.");

        // Identifiers come from the EF model; values are parameters. A concurrent delivery of the
        // same message waits for this row and then inserts nothing.
        var sql =
            $"INSERT INTO \"{entityType.GetSchema()}\".\"{entityType.GetTableName()}\" " +
            "(\"MessageId\", \"Consumer\", \"ProcessedAt\") VALUES ({0}, {1}, {2}) ON CONFLICT DO NOTHING";

        var inserted = await dbContext.Database.ExecuteSqlRawAsync(
            sql,
            [messageId, GetType().FullName!, DateTimeOffset.UtcNow],
            cancellationToken);

        return inserted == 1;
    }
}
