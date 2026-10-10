using Ledger.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application.Operations;

/// <summary>
/// Shared operations touch several users' books: none of them may fall in a period one of those
/// users closed (their lock date). The ledger checks it again when it writes.
/// </summary>
internal sealed class BooksLockGuard(IDebtsDbContext dbContext)
{
    public async Task EnsureOpenAsync(
        IEnumerable<Guid> userIds,
        IEnumerable<DateTimeOffset> dates,
        Guid authorUserId,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        var when = dates.ToList();

        var locks = await dbContext.BooksLocks.AsNoTracking()
            .Where(l => ids.Contains(l.UserId) && l.LockedUntil != null)
            .ToListAsync(cancellationToken);

        var locked = locks.FirstOrDefault(l => l.UserId == authorUserId && when.Any(l.Covers))
                     ?? locks.FirstOrDefault(l => when.Any(l.Covers));

        if (locked is null)
            return;

        var until = locked.LockedUntil!.Value.ToString("yyyy-MM-dd HH:mm 'UTC'");

        if (locked.UserId == authorUserId)
            throw new DomainException("books.locked", $"Your books are closed up to {until}.");

        var name = await dbContext.Users.AsNoTracking()
            .Where(u => u.UserId == locked.UserId)
            .Select(u => u.DisplayName)
            .SingleOrDefaultAsync(cancellationToken) ?? "Another person";

        throw new DomainException(
            "books.locked_for_other",
            $"{name} closed their books up to {until}: the operation cannot change that period.");
    }
}
