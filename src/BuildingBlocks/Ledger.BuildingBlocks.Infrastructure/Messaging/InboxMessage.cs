namespace Ledger.BuildingBlocks.Infrastructure.Messaging;

/// <summary>A message already handled by a consumer of the module (inbox, for idempotency).</summary>
public sealed class InboxMessage
{
    private InboxMessage()
    {
        Consumer = null!;
    }

    public Guid MessageId { get; private set; }
    public string Consumer { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }
}
