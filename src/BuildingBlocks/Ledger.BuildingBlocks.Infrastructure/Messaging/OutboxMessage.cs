namespace Ledger.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// An integration event waiting to be published. It is stored in the module's schema, in the same
/// database transaction as the business change that raised it (transactional outbox).
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        Type = null!;
        Content = null!;
    }

    public Guid Id { get; private set; }
    public string Type { get; private set; }
    public string Content { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage Create(string type, string content)
        => new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            Content = content,
            OccurredAt = DateTimeOffset.UtcNow
        };

    public void MarkProcessed() => ProcessedAt = DateTimeOffset.UtcNow;

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
    }
}
