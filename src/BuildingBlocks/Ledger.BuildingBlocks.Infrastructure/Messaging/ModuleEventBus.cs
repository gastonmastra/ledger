using System.Text.Json;
using Ledger.BuildingBlocks.Application;
using Microsoft.EntityFrameworkCore;

namespace Ledger.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Writes integration events to the outbox of the module DbContext. They are sent to RabbitMQ by
/// <see cref="OutboxProcessor{TDbContext}"/> only after <c>SaveChangesAsync</c> commits them.
/// </summary>
public abstract class ModuleEventBus<TDbContext>(TDbContext dbContext) : IEventBus
    where TDbContext : DbContext
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : class
    {
        var type = @event.GetType();

        dbContext.Set<OutboxMessage>().Add(OutboxMessage.Create(
            type.AssemblyQualifiedName!,
            JsonSerializer.Serialize(@event, type, OutboxSerializer.Options)));

        return Task.CompletedTask;
    }
}

internal static class OutboxSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
