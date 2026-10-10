using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Transactions.Domain;

/// <summary>
/// "My books are closed up to this date": nothing dated on or before it can be recorded,
/// changed, deleted or restored in the user's books.
/// </summary>
public sealed class BooksLock
{
    private BooksLock()
    {
    }

    public Guid OwnerId { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static BooksLock For(Guid ownerId) => new() { OwnerId = ownerId, UpdatedAt = DateTimeOffset.UtcNow };

    public void Set(DateTimeOffset? lockedUntil)
    {
        LockedUntil = lockedUntil;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool Covers(DateTimeOffset date) => LockedUntil is { } until && date <= until;

    public void EnsureOpen(DateTimeOffset date)
    {
        if (Covers(date))
            throw new DomainException(
                "books.locked",
                FormattableString.Invariant(
                    $"Your books are closed up to {LockedUntil:yyyy-MM-dd HH:mm} (UTC). Move the lock date back to change that period."));
    }
}
