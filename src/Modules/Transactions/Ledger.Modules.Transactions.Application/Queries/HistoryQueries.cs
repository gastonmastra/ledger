using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Queries;

/// <summary>How a transaction stands today.</summary>
public enum TransactionStatus
{
    /// <summary>The version that counts.</summary>
    Current,
    /// <summary>Replaced by a newer revision.</summary>
    Changed,
    /// <summary>Deleted (reversed without replacement).</summary>
    Deleted,
    /// <summary>A reversal that cancels another transaction.</summary>
    Reversal
}

/// <param name="ChangedBy">Who made the change: "me", a contact's name, or null for the system.</param>
public sealed record HistoryItemDto(
    Guid Id,
    int Revision,
    TransactionKind Kind,
    TransactionStatus Status,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    string? Description,
    ChangeAction? Action,
    string? Reason,
    Guid? ChangedByUserId,
    bool ChangedByMe,
    string? ChangedByName,
    IReadOnlyList<PostingDto> Postings);

public sealed record GetTransactionHistoryQuery(Guid UserId, Guid TransactionId) : IQuery<IReadOnlyList<HistoryItemDto>>;

public sealed record ActivityItemDto(
    Guid TransactionId,
    Guid EntryId,
    TransactionKind Kind,
    ChangeAction Action,
    string? Description,
    string Reason,
    DateTimeOffset At,
    bool ChangedByMe,
    string? ChangedByName);

public sealed record GetTransactionActivityQuery(Guid UserId, int Page = 1, int PageSize = 30)
    : IQuery<PagedResult<ActivityItemDto>>;

internal sealed class HistoryQueryHandlers(ITransactionsDbContext dbContext, TransactionMapper mapper)
    : IQueryHandler<GetTransactionHistoryQuery, IReadOnlyList<HistoryItemDto>>,
      IQueryHandler<GetTransactionActivityQuery, PagedResult<ActivityItemDto>>
{
    public async Task<IReadOnlyList<HistoryItemDto>> Handle(GetTransactionHistoryQuery query, CancellationToken cancellationToken)
    {
        var id = new TransactionId(query.TransactionId);
        var target = await dbContext.Transactions.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (target is null || target.OwnerId != query.UserId)
            throw new NotFoundException("Transaction", query.TransactionId);

        var entry = await dbContext.Transactions
            .AsNoTracking()
            .Include(t => t.Postings)
            .Where(t => t.EntryId == target.EntryId && t.OwnerId == query.UserId)
            // Each version followed by the reversal that cancelled it (they share the revision number).
            .OrderBy(t => t.Revision)
            .ThenBy(t => t.Kind == TransactionKind.Reversal || t.Kind == TransactionKind.OpeningBalanceReversal)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = await mapper.ToDtosAsync(query.UserId, entry, cancellationToken);
        var names = await NamesAsync(query.UserId, cancellationToken);

        return entry
            .Zip(dtos)
            .Select(pair => new HistoryItemDto(
                pair.First.Id.Value,
                pair.First.Revision,
                pair.First.Kind,
                pair.Second.Status,
                pair.First.OccurredAt,
                pair.First.CreatedAt,
                pair.First.Description,
                pair.First.ChangeAction,
                pair.First.ChangeReason,
                pair.First.ChangedByUserId,
                pair.First.ChangedByUserId == query.UserId,
                pair.First.ChangedByUserId is { } by ? names.GetValueOrDefault(by) : null,
                pair.Second.Postings))
            .ToList();
    }

    public async Task<PagedResult<ActivityItemDto>> Handle(GetTransactionActivityQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        // Deletions and edits are represented by their reversal; the new revision of an edit is not repeated.
        // Changes of shared operations (loans, splits…) are listed by the Debts module, once per change.
        var own = OwnKinds;
        var changes = dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.OwnerId == query.UserId
                        && t.ChangeAction != null
                        && ((t.Kind == TransactionKind.Reversal
                             && (t.ChangeAction == ChangeAction.System
                                 || dbContext.Transactions.Any(o => o.Id == t.ReversalOfId && own.Contains(o.Kind))))
                            || (t.ChangeAction == ChangeAction.Restore && own.Contains(t.Kind))));

        var total = await changes.CountAsync(cancellationToken);
        var items = await changes
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var reversedIds = items.Where(t => t.ReversalOfId != null).Select(t => t.ReversalOfId!.Value).ToList();
        var originals = await dbContext.Transactions.AsNoTracking()
            .Where(t => reversedIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Kind, cancellationToken);
        var names = await NamesAsync(query.UserId, cancellationToken);

        return new PagedResult<ActivityItemDto>(
            items.Select(t => new ActivityItemDto(
                    t.Id.Value,
                    t.EntryId.Value,
                    t.ReversalOfId is { } of && originals.TryGetValue(of, out var kind) ? kind : t.Kind,
                    t.ChangeAction!.Value,
                    t.Description,
                    t.ChangeReason!,
                    t.CreatedAt,
                    t.ChangedByUserId == query.UserId,
                    t.ChangedByUserId is { } by ? names.GetValueOrDefault(by) : null))
                .ToList(),
            page,
            pageSize,
            total);
    }

    private static readonly TransactionKind[] OwnKinds =
    [
        TransactionKind.Income, TransactionKind.Expense, TransactionKind.Transfer,
        TransactionKind.OpeningBalance, TransactionKind.BalanceAdjustment
    ];

    /// <summary>Names of the people (registered users are known by their person id) in the user's books.</summary>
    private async Task<Dictionary<Guid, string>> NamesAsync(Guid userId, CancellationToken cancellationToken)
        => await dbContext.Counterparties.AsNoTracking()
            .Where(c => c.OwnerId == userId && c.Kind == CounterpartyKind.Contact)
            .ToDictionaryAsync(c => c.PersonId, c => c.Name, cancellationToken);
}
