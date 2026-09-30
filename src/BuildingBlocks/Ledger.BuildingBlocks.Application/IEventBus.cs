namespace Ledger.BuildingBlocks.Application;

/// <summary>
/// Publishes integration events. Every module declares its own interface deriving from this
/// one, so events are written to that module's outbox, in the same database transaction as
/// the change that produced them.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : class;
}
