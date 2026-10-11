using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application.Operations;

/// <summary>The open debts between two people that a payment can pay, with what is still available on each.</summary>
/// <param name="Debts">The debts themselves (tracked), by id.</param>
/// <param name="Descriptions">Description and date of the operation that created each debt, by debt id.</param>
internal sealed record PayableDebts(
    IReadOnlyList<OpenDebt> Open,
    IReadOnlyDictionary<Guid, Debt> Debts,
    IReadOnlyDictionary<Guid, (string? Description, DateTimeOffset OccurredAt)> Origins);

/// <summary>Reads the debts a payment between two people applies to (outside groups, one currency).</summary>
internal sealed class PaymentBook(IDebtsDbContext dbContext)
{
    /// <param name="replacingSettlementId">
    /// The payment being edited: what it paid is available again and the debt it created (if it paid more than
    /// what was owed) is left out, because the new version replaces both.
    /// </param>
    public async Task<PayableDebts> PayableAsync(
        Guid personA,
        Guid personB,
        Currency currency,
        Guid? replacingSettlementId,
        CancellationToken cancellationToken)
    {
        var debts = await dbContext.Debts
            .Where(d => d.GroupId == null
                        && d.Currency == currency
                        && (d.Status == DebtStatus.Open || d.Status == DebtStatus.Settled)
                        && d.OperationId != replacingSettlementId
                        && ((d.CreditorPersonId == personA && d.DebtorPersonId == personB)
                            || (d.CreditorPersonId == personB && d.DebtorPersonId == personA)))
            .ToListAsync(cancellationToken);

        var debtIds = debts.Select(d => d.Id).ToList();

        // Paid by the version being replaced: owed again in the new version.
        var freed = replacingSettlementId is { } replacing
            ? await dbContext.Payments.AsNoTracking()
                .Where(p => p.SettlementOperationId == replacing && debtIds.Contains(p.DebtId))
                .GroupBy(p => p.DebtId)
                .Select(g => new { g.Key, Amount = g.Sum(p => p.Amount) })
                .ToDictionaryAsync(x => x.Key, x => x.Amount, cancellationToken)
            : [];

        // Reserved by payments still being recorded.
        var pendingIds = dbContext.Operations
            .Where(o => o.Status == OperationStatus.Pending && o.Kind == OperationKind.Settlement)
            .Select(o => o.Id);

        var reserved = await dbContext.Payments.AsNoTracking()
            .Where(p => debtIds.Contains(p.DebtId)
                        && p.SettlementOperationId != replacingSettlementId
                        && pendingIds.Contains(p.SettlementOperationId))
            .GroupBy(p => p.DebtId)
            .Select(g => new { g.Key, Amount = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Amount, cancellationToken);

        var operationIds = debts.Select(d => d.OperationId).Distinct().ToList();
        var operations = await dbContext.Operations.AsNoTracking()
            .Where(o => operationIds.Contains(o.Id))
            .Select(o => new { o.Id, o.OccurredAt, o.Description })
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var open = debts
            .Select(d => new OpenDebt(
                d.Id,
                d.CreditorPersonId,
                d.DebtorPersonId,
                d.Remaining + freed.GetValueOrDefault(d.Id) - reserved.GetValueOrDefault(d.Id),
                operations.TryGetValue(d.OperationId, out var o) ? o.OccurredAt : d.CreatedAt,
                d.CreatedAt))
            .Where(d => d.Available > 0)
            .OrderBy(d => d.OccurredAt).ThenBy(d => d.CreatedAt).ThenBy(d => d.DebtId)
            .ToList();

        return new PayableDebts(
            open,
            debts.ToDictionary(d => d.Id),
            debts.ToDictionary(
                d => d.Id,
                d => operations.TryGetValue(d.OperationId, out var o) ? (o.Description, o.OccurredAt) : ((string?)null, d.CreatedAt)));
    }

    /// <summary>The payment rows of a settlement (stored or just added).</summary>
    public async Task<IReadOnlyList<DebtPayment>> RowsOfAsync(Guid settlementId, CancellationToken cancellationToken)
    {
        var stored = await dbContext.Payments.Where(p => p.SettlementOperationId == settlementId).ToListAsync(cancellationToken);
        return stored.Union(dbContext.Payments.Local.Where(p => p.SettlementOperationId == settlementId)).ToList();
    }

    /// <summary>
    /// Applies (completed version) or takes back (replaced or deleted version) what a payment paid. The debts it
    /// created itself are handled with the operation's own debts.
    /// </summary>
    public async Task ApplyAsync(Guid settlementId, bool undo, CancellationToken cancellationToken)
    {
        var rows = await RowsOfAsync(settlementId, cancellationToken);

        if (rows.Count == 0)
            return;

        var debtIds = rows.Select(r => r.DebtId).Distinct().ToList();
        var debts = await dbContext.Debts.Where(d => debtIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);

        foreach (var row in rows)
        {
            if (!debts.TryGetValue(row.DebtId, out var debt))
                continue;

            if (undo)
                debt.RemovePayment(row.Amount);
            else
                debt.ApplyPayment(row.Amount);
        }
    }

    /// <summary>True when some current or pending payment paid (part of) one of these debts.</summary>
    public async Task<bool> AnyPaidAsync(IReadOnlyCollection<Guid> debtIds, CancellationToken cancellationToken)
    {
        if (debtIds.Count == 0)
            return false;

        var current = dbContext.Operations
            .Where(o => o.Status == OperationStatus.Pending || o.Status == OperationStatus.Completed)
            .Select(o => o.Id);

        return await dbContext.Payments.AnyAsync(
            p => debtIds.Contains(p.DebtId) && current.Contains(p.SettlementOperationId),
            cancellationToken);
    }

    /// <summary>A payment between the two people is being recorded or changed.</summary>
    public async Task<bool> PaymentInProgressAsync(Guid personA, Guid personB, Currency currency, Guid? exceptOperationId, CancellationToken cancellationToken)
        => await dbContext.Operations.AnyAsync(
            o => o.Kind == OperationKind.Settlement
                 && o.GroupId == null
                 && o.Currency == currency
                 && o.Id != exceptOperationId
                 && ((o.FromPersonId == personA && o.ToPersonId == personB) || (o.FromPersonId == personB && o.ToPersonId == personA))
                 && (o.Status == OperationStatus.Pending || (o.Status == OperationStatus.Completed && o.PendingChangeId != null)),
            cancellationToken);
}
