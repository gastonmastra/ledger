using System.Text.Json;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.Modules.Debts.Application.Operations;
using Ledger.Modules.Debts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application.Queries;

public sealed record PersonAmountDto(Guid PersonId, string Name, decimal Amount, Guid? WalletId = null);

/// <summary>A debt a payment paid (part of), from the viewer's point of view.</summary>
/// <param name="OperationId">The loan, split or existing debt that created the debt.</param>
/// <param name="DebtAmount">The original amount of the debt.</param>
public sealed record PaymentAllocationDto(
    Guid DebtId,
    Guid OperationId,
    string? Description,
    DateTimeOffset OccurredAt,
    Queries.DebtDirection Direction,
    decimal DebtAmount,
    decimal Amount);

public sealed record PendingChangeDto(
    Guid ChangeId,
    ChangeKind Kind,
    string RequestedByName,
    bool RequestedByMe,
    string Reason,
    IReadOnlyList<string> WaitingFor,
    bool WaitingForMe);

/// <summary>
/// A loan, existing debt, split or settlement, from the viewer's point of view.
/// ContactId, WalletId and CategoryId are only shown to whoever recorded it (they are theirs).
/// </summary>
/// <param name="Direction">Existing debts: whether the viewer owes or is owed.</param>
/// <param name="BlockedReason">Why it cannot be edited or deleted now (an error code), if so.</param>
/// <param name="SettlementOperationIds">Settlements (payments) to change or undo before changing it.</param>
/// <param name="Allocations">Payments: the debts it paid and how much of each.</param>
/// <param name="ExtraAmount">Payments: what it paid beyond what was owed (a debt of the payee to the payer).</param>
public sealed record OperationDetailsDto(
    Guid Id,
    Guid EntryId,
    int Revision,
    OperationKind Kind,
    OperationStatus Status,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset OccurredAt,
    string? Description,
    string Currency,
    decimal Total,
    Guid? GroupId,
    string? GroupName,
    SplitMethod? Method,
    SettlementScope? Scope,
    bool RecordedByMe,
    string RecordedByName,
    Guid? FromPersonId,
    string? FromName,
    Guid? ToPersonId,
    string? ToName,
    Guid? CounterpartyPersonId,
    string? CounterpartyName,
    Guid? ContactId,
    InitialDebtDirection? Direction,
    Guid? WalletId,
    Guid? CategoryId,
    IReadOnlyList<PersonAmountDto> Payers,
    IReadOnlyList<PersonAmountDto> Shares,
    bool IsDisputed,
    PendingChangeDto? PendingChange,
    bool CanEdit,
    bool CanDelete,
    bool CanRestore,
    string? BlockedReason,
    IReadOnlyList<Guid> SettlementOperationIds,
    IReadOnlyList<PaymentAllocationDto>? Allocations = null,
    decimal ExtraAmount = 0);

/// <param name="IncludeHistory">Also replaced, deleted and refused versions.</param>
public sealed record GetOperationsQuery(
    Guid UserId,
    OperationKind? Kind = null,
    Guid? GroupId = null,
    Guid? PersonId = null,
    bool IncludeHistory = false,
    int Page = 1,
    int PageSize = 30) : IQuery<PagedResult<OperationDetailsDto>>;

public sealed record GetOperationQuery(Guid UserId, Guid OperationId) : IQuery<OperationDetailsDto>;

public sealed record ApprovalDto(Guid UserId, string Name, ApprovalStatus Status, string? Reason, DateTimeOffset? RespondedAt);

/// <param name="Current">The version it changes. Proposed: the new one (null for a delete).</param>
public sealed record ChangeDto(
    Guid Id,
    Guid OperationId,
    Guid EntryId,
    OperationKind OperationKind,
    ChangeKind Kind,
    ChangeStatus Status,
    string RequestedByName,
    bool RequestedByMe,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AppliedAt,
    string? FailureReason,
    Guid? NewOperationId,
    IReadOnlyList<ApprovalDto> Approvals,
    ApprovalStatus? MyApproval,
    bool Disputed,
    ChangeSnapshotDto Current,
    ChangeSnapshotDto? Proposed);

public sealed record ChangeSnapshotDto(
    decimal Total,
    string Currency,
    DateTimeOffset OccurredAt,
    string? Description,
    string? FromName,
    string? ToName,
    IReadOnlyList<PersonAmountDto> Payers,
    IReadOnlyList<PersonAmountDto> Shares);

public enum ChangesFilter
{
    /// <summary>Changes waiting for my answer.</summary>
    ToReview,
    /// <summary>Every change I requested or was asked about.</summary>
    All
}

public sealed record GetChangesQuery(Guid UserId, ChangesFilter Filter = ChangesFilter.ToReview) : IQuery<IReadOnlyList<ChangeDto>>;

/// <summary>Every version of an operation and every change requested on it.</summary>
public sealed record OperationHistoryDto(IReadOnlyList<OperationDetailsDto> Versions, IReadOnlyList<ChangeDto> Changes);

public sealed record GetOperationHistoryQuery(Guid UserId, Guid OperationId) : IQuery<OperationHistoryDto>;

public sealed record GetDebtActivityQuery(Guid UserId, int Page = 1, int PageSize = 30) : IQuery<PagedResult<ChangeDto>>;

internal sealed class OperationQueryHandlers(IDebtsDbContext dbContext, PeopleDirectory people)
    : IQueryHandler<GetOperationsQuery, PagedResult<OperationDetailsDto>>,
      IQueryHandler<GetOperationQuery, OperationDetailsDto>,
      IQueryHandler<GetOperationHistoryQuery, OperationHistoryDto>,
      IQueryHandler<GetChangesQuery, IReadOnlyList<ChangeDto>>,
      IQueryHandler<GetDebtActivityQuery, PagedResult<ChangeDto>>
{
    private static readonly OperationStatus[] CurrentStatuses = [OperationStatus.Pending, OperationStatus.Completed];

    public async Task<PagedResult<OperationDetailsDto>> Handle(GetOperationsQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var operations = Visible(query.UserId);

        if (!query.IncludeHistory)
            operations = operations.Where(o => CurrentStatuses.Contains(o.Status));

        if (query.Kind is { } kind)
            operations = operations.Where(o => o.Kind == kind);

        if (query.GroupId is { } groupId)
            operations = operations.Where(o => o.GroupId == groupId);

        if (query.PersonId is { } personId)
            operations = operations.Where(o => o.FromPersonId == personId
                                               || o.ToPersonId == personId
                                               || o.Payers.Any(p => p.PersonId == personId)
                                               || o.Shares.Any(s => s.PersonId == personId));

        var total = await operations.CountAsync(cancellationToken);
        var items = await operations
            .OrderByDescending(o => o.OccurredAt).ThenByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<OperationDetailsDto>(
            await ToDtosAsync(query.UserId, items, cancellationToken), page, pageSize, total);
    }

    public async Task<OperationDetailsDto> Handle(GetOperationQuery query, CancellationToken cancellationToken)
    {
        var operation = await Visible(query.UserId).SingleOrDefaultAsync(o => o.Id == query.OperationId, cancellationToken)
                        ?? throw new NotFoundException("Operation", query.OperationId);

        return (await ToDtosAsync(query.UserId, [operation], cancellationToken))[0];
    }

    public async Task<OperationHistoryDto> Handle(GetOperationHistoryQuery query, CancellationToken cancellationToken)
    {
        var target = await Visible(query.UserId).SingleOrDefaultAsync(o => o.Id == query.OperationId, cancellationToken)
                     ?? throw new NotFoundException("Operation", query.OperationId);

        var versions = await dbContext.Operations.AsNoTracking()
            .Where(o => o.EntryId == target.EntryId)
            .OrderBy(o => o.Revision).ThenBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        var versionIds = versions.Select(v => v.Id).ToList();

        var changes = await dbContext.Changes.AsNoTracking()
            .Include(c => c.Approvals)
            .Where(c => versionIds.Contains(c.OperationId))
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return new OperationHistoryDto(
            await ToDtosAsync(query.UserId, versions, cancellationToken),
            await ToChangeDtosAsync(query.UserId, changes, cancellationToken));
    }

    public async Task<IReadOnlyList<ChangeDto>> Handle(GetChangesQuery query, CancellationToken cancellationToken)
    {
        var me = query.UserId;
        var changes = dbContext.Changes.AsNoTracking().Include(c => c.Approvals).AsQueryable();

        changes = query.Filter == ChangesFilter.ToReview
            ? changes.Where(c => c.Status == ChangeStatus.Pending
                                 && c.Approvals.Any(a => a.UserId == me && a.Status == ApprovalStatus.Pending))
            : changes.Where(c => c.RequestedByUserId == me || c.Approvals.Any(a => a.UserId == me));

        var items = await changes.OrderByDescending(c => c.CreatedAt).Take(200).ToListAsync(cancellationToken);

        return await ToChangeDtosAsync(me, items, cancellationToken);
    }

    public async Task<PagedResult<ChangeDto>> Handle(GetDebtActivityQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var me = query.UserId;

        var changes = dbContext.Changes.AsNoTracking()
            .Include(c => c.Approvals)
            .Where(c => c.RequestedByUserId == me || c.Approvals.Any(a => a.UserId == me));

        var total = await changes.CountAsync(cancellationToken);
        var items = await changes
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ChangeDto>(await ToChangeDtosAsync(me, items, cancellationToken), page, pageSize, total);
    }

    /// <summary>Operations the user is part of, or of a group they belong to.</summary>
    private IQueryable<DebtOperation> Visible(Guid userId)
    {
        var myGroups = dbContext.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId);

        return dbContext.Operations.AsNoTracking()
            .Where(o => o.InitiatorUserId == userId
                        || o.FromPersonId == userId
                        || o.ToPersonId == userId
                        || o.Payers.Any(p => p.PersonId == userId)
                        || o.Shares.Any(s => s.PersonId == userId)
                        || (o.GroupId != null && myGroups.Contains(o.GroupId.Value)));
    }

    private async Task<IReadOnlyList<OperationDetailsDto>> ToDtosAsync(
        Guid me,
        IReadOnlyList<DebtOperation> operations,
        CancellationToken cancellationToken)
    {
        if (operations.Count == 0)
            return [];

        var names = await NamesAsync(me, operations.Select(o => o.InitiatorUserId), cancellationToken);
        var ids = operations.Select(o => o.Id).ToList();

        var debts = await dbContext.Debts.AsNoTracking()
            .Where(d => ids.Contains(d.OperationId))
            .ToListAsync(cancellationToken);

        // Payments that paid the debts of these operations (only current ones block a change).
        var debtIds = debts.Select(d => d.Id).ToList();
        var currentIds = dbContext.Operations
            .Where(o => o.Status == OperationStatus.Pending || o.Status == OperationStatus.Completed)
            .Select(o => o.Id);
        var paidBy = await dbContext.Payments.AsNoTracking()
            .Where(p => debtIds.Contains(p.DebtId) && currentIds.Contains(p.SettlementOperationId))
            .Select(p => new { p.DebtId, p.SettlementOperationId })
            .ToListAsync(cancellationToken);

        // What the payments among these operations paid.
        var rows = await dbContext.Payments.AsNoTracking()
            .Where(p => ids.Contains(p.SettlementOperationId))
            .ToListAsync(cancellationToken);
        var rowDebtIds = rows.Select(r => r.DebtId).Distinct().ToList();
        var paidDebts = await dbContext.Debts.AsNoTracking()
            .Where(d => rowDebtIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);
        var paidDebtOperationIds = paidDebts.Values.Select(d => d.OperationId).Distinct().ToList();
        var paidDebtOperations = await dbContext.Operations.AsNoTracking()
            .Where(o => paidDebtOperationIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Description, o.OccurredAt })
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var myContacts = await dbContext.Contacts.AsNoTracking()
            .Where(c => c.OwnerId == me)
            .ToListAsync(cancellationToken);

        var changeIds = operations.Where(o => o.PendingChangeId != null).Select(o => o.PendingChangeId!.Value).ToList();
        var pendingChanges = await dbContext.Changes.AsNoTracking()
            .Include(c => c.Approvals)
            .Where(c => changeIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var entryIds = operations.Select(o => o.EntryId).Distinct().ToList();
        var latestRevisions = await dbContext.Operations.AsNoTracking()
            .Where(o => entryIds.Contains(o.EntryId) && o.Status != OperationStatus.Rejected)
            .GroupBy(o => o.EntryId)
            .Select(g => new { g.Key, Latest = g.Max(o => o.Revision) })
            .ToDictionaryAsync(x => x.Key, x => x.Latest, cancellationToken);

        string Name(Guid? personId) => personId is { } id ? names.GetValueOrDefault(id) ?? "(unknown)" : "(unknown)";

        return operations.Select(o =>
        {
            var mine = o.InitiatorUserId == me;
            var counterparty = o.Kind is OperationKind.Loan or OperationKind.InitialDebt or OperationKind.Settlement && o.GroupId is null
                ? o.FromPersonId == me ? o.ToPersonId : o.ToPersonId == me ? o.FromPersonId : null
                : null;

            var opDebts = debts.Where(d => d.OperationId == o.Id).ToList();
            var opDebtIds = opDebts.Select(d => d.Id).ToHashSet();
            var settlements = opDebts
                .Where(d => d.SettlementOperationId != null && (d.Status == DebtStatus.Settled || d.IsSettling))
                .Select(d => d.SettlementOperationId!.Value)
                .Concat(paidBy.Where(p => opDebtIds.Contains(p.DebtId)).Select(p => p.SettlementOperationId))
                .Distinct()
                .ToList();

            string? blocked = null;

            // Group settlements never block; a payment is blocked when the debt it created was paid.
            if (o.PendingChangeId is not null)
                blocked = "operation.change_pending";
            else if (!(o.Kind == OperationKind.Settlement && o.GroupId is not null) && settlements.Count != 0)
                blocked = "debt.settled";

            List<PaymentAllocationDto>? allocations = null;

            if (o.Kind == OperationKind.Settlement && o.GroupId is null)
            {
                allocations = rows
                    .Where(r => r.SettlementOperationId == o.Id && paidDebts.ContainsKey(r.DebtId))
                    .Select(r =>
                    {
                        var debt = paidDebts[r.DebtId];
                        var origin = paidDebtOperations.GetValueOrDefault(debt.OperationId);

                        return new PaymentAllocationDto(
                            debt.Id,
                            debt.OperationId,
                            origin?.Description,
                            origin?.OccurredAt ?? debt.CreatedAt,
                            debt.CreditorPersonId == me ? Queries.DebtDirection.OwedToMe : Queries.DebtDirection.IOwe,
                            debt.Amount,
                            r.Amount);
                    })
                    .OrderBy(a => a.OccurredAt)
                    .ToList();
            }

            var isLatest = latestRevisions.GetValueOrDefault(o.EntryId) == o.Revision;
            var isCurrent = o.Status == OperationStatus.Completed;

            PendingChangeDto? pending = null;

            if (o.PendingChangeId is { } changeId && pendingChanges.TryGetValue(changeId, out var change))
            {
                var waiting = change.Approvals.Where(a => a.Status == ApprovalStatus.Pending).ToList();

                pending = new PendingChangeDto(
                    change.Id,
                    change.Kind,
                    Name(change.RequestedByUserId),
                    change.RequestedByUserId == me,
                    change.Reason,
                    waiting.Select(a => Name(a.UserId)).ToList(),
                    waiting.Any(a => a.UserId == me));
            }

            return new OperationDetailsDto(
                o.Id,
                o.EntryId,
                o.Revision,
                o.Kind,
                o.Status,
                o.RejectionReason,
                o.CreatedAt,
                o.CompletedAt,
                o.OccurredAt,
                o.Description,
                o.Currency.ToString(),
                o.Total,
                o.GroupId,
                o.GroupId is { } g ? names.GetValueOrDefault(g) : null,
                o.Method,
                o.Scope,
                mine,
                Name(o.InitiatorUserId),
                o.FromPersonId,
                o.FromPersonId is null ? null : Name(o.FromPersonId),
                o.ToPersonId,
                o.ToPersonId is null ? null : Name(o.ToPersonId),
                counterparty,
                counterparty is null ? null : Name(counterparty),
                mine && counterparty is { } cp ? myContacts.FirstOrDefault(c => c.PersonId == cp)?.ContactId : null,
                o.Kind == OperationKind.InitialDebt
                    ? o.FromPersonId == me ? InitialDebtDirection.IOwe : InitialDebtDirection.TheyOweMe
                    : null,
                mine ? o.WalletId ?? (o.Kind == OperationKind.Loan ? o.Payers.FirstOrDefault()?.WalletId : null) : null,
                mine ? o.CategoryId : null,
                o.Payers.Select(p => new PersonAmountDto(p.PersonId, Name(p.PersonId), p.Amount, mine && p.PersonId == me ? p.WalletId : null)).ToList(),
                o.Shares.Select(s => new PersonAmountDto(s.PersonId, Name(s.PersonId), s.Amount)).ToList(),
                o.IsDisputed,
                pending,
                isCurrent && blocked is null,
                isCurrent && blocked is null,
                o.Status == OperationStatus.Deleted && isLatest && o.PendingChangeId is null && o.Kind != OperationKind.Settlement,
                blocked,
                settlements,
                allocations,
                o.Kind == OperationKind.Settlement && o.GroupId is null
                    ? opDebts.Where(d => d.Status is DebtStatus.Pending or DebtStatus.Open or DebtStatus.Settled).Sum(d => d.Amount)
                    : 0);
        }).ToList();
    }

    private async Task<IReadOnlyList<ChangeDto>> ToChangeDtosAsync(
        Guid me,
        IReadOnlyList<OperationChange> changes,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
            return [];

        var operationIds = changes.Select(c => c.OperationId).Distinct().ToList();
        var operations = await dbContext.Operations.AsNoTracking()
            .Where(o => operationIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var userIds = changes.Select(c => c.RequestedByUserId)
            .Concat(changes.SelectMany(c => c.Approvals.Select(a => a.UserId)))
            .Concat(operations.Values.Select(o => o.InitiatorUserId));
        var names = await NamesAsync(me, userIds, cancellationToken);

        string Name(Guid? personId) => personId is { } id ? names.GetValueOrDefault(id) ?? "(unknown)" : "(unknown)";

        ChangeSnapshotDto Snapshot(OperationSnapshot s) => new(
            s.Total,
            s.Currency,
            s.OccurredAt,
            s.Description,
            s.FromPersonId is null ? null : Name(s.FromPersonId),
            s.ToPersonId is null ? null : Name(s.ToPersonId),
            s.Payers.Select(p => new PersonAmountDto(p.PersonId, Name(p.PersonId), p.Amount)).ToList(),
            s.Shares.Select(p => new PersonAmountDto(p.PersonId, Name(p.PersonId), p.Amount)).ToList());

        return changes.Select(c =>
        {
            var operation = operations[c.OperationId];
            var proposed = c.Preview is null
                ? null
                : JsonSerializer.Deserialize<OperationSnapshot>(c.Preview, ChangeHandlers.Json);

            return new ChangeDto(
                c.Id,
                c.OperationId,
                operation.EntryId,
                operation.Kind,
                c.Kind,
                c.Status,
                Name(c.RequestedByUserId),
                c.RequestedByUserId == me,
                c.Reason,
                c.CreatedAt,
                c.AppliedAt,
                c.FailureReason,
                c.NewOperationId,
                c.Approvals
                    .Select(a => new ApprovalDto(a.UserId, Name(a.UserId), a.Status, a.Reason, a.RespondedAt))
                    .ToList(),
                c.Approvals.FirstOrDefault(a => a.UserId == me)?.Status,
                c.AnyRejected,
                Snapshot(OperationSnapshot.Of(operation)),
                proposed is null ? null : Snapshot(proposed));
        }).ToList();
    }

    /// <summary>My names for people and groups, plus registered users' own names for anybody else.</summary>
    private async Task<Dictionary<Guid, string>> NamesAsync(
        Guid me,
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var names = await people.NamesAsync(me, cancellationToken);
        var missing = userIds.Distinct().Where(id => !names.ContainsKey(id)).ToList();

        foreach (var user in await dbContext.Users.AsNoTracking()
                     .Where(u => missing.Contains(u.UserId))
                     .ToListAsync(cancellationToken))
            names[user.UserId] = user.DisplayName;

        return names;
    }
}
