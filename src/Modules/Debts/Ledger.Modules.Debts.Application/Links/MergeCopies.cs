using Ledger.Modules.Debts.Domain;
using Ledger.Modules.Transactions.Contracts;

namespace Ledger.Modules.Debts.Application.Links;

/// <summary>One operation of the shared history, as it is copied into the books of the linked user.</summary>
public sealed record MergeCopy(DebtOperation Operation, LedgerLeg Leg);

/// <summary>
/// What linking a person to a registered user copies into the user's books: for every current operation involving
/// the person, their positions (what they are owed and owe, per person or per group) against Opening balances.
/// No money moved in their books when it happened, so a copy is neither income nor expense; settled pairs cancel out.
/// Each copy is correlated to its operation, so later edits, deletes and restores update the user's books normally.
/// </summary>
internal static class MergeCopies
{
    /// <param name="operations">Current (completed) operations involving the person.</param>
    /// <param name="debts">Every debt involving the person.</param>
    public static IReadOnlyList<MergeCopy> Build(
        IEnumerable<DebtOperation> operations,
        IReadOnlyList<Debt> debts,
        Guid personId,
        Guid userId)
    {
        var copies = new List<MergeCopy>();

        foreach (var operation in operations.OrderBy(o => o.OccurredAt).ThenBy(o => o.CreatedAt))
        {
            var positions = PositionsOf(operation, debts, personId).Where(p => p.Amount != 0).ToList();

            if (positions.Count == 0)
                continue;

            copies.Add(new MergeCopy(
                operation,
                new LedgerLeg(userId, null, null, 0, 0, positions, OpeningBalanceAmount: -positions.Sum(p => p.Amount))));
        }

        return copies;
    }

    /// <summary>The person's positions in one operation, debit-signed like <see cref="LedgerLegs"/>.</summary>
    public static IReadOnlyList<LedgerPosition> PositionsOf(DebtOperation operation, IReadOnlyList<Debt> debts, Guid personId)
    {
        if (operation.GroupId is { } groupId)
            return GroupPositions(operation, groupId, personId);

        if (operation.Kind == OperationKind.Settlement)
        {
            // A settlement cancels the debts it paid.
            return debts
                .Where(d => d.SettlementOperationId == operation.Id && d.Status == DebtStatus.Settled && d.Involves(personId))
                .Select(d => d.CreditorPersonId == personId
                    ? new LedgerPosition(d.DebtorPersonId, PositionSides.Receivable, -d.Amount)
                    : new LedgerPosition(d.CreditorPersonId, PositionSides.Payable, d.Amount))
                .ToList();
        }

        return debts
            .Where(d => d.OperationId == operation.Id
                        && d.Status is DebtStatus.Open or DebtStatus.Settled
                        && d.Involves(personId))
            .Select(d => d.CreditorPersonId == personId
                ? new LedgerPosition(d.DebtorPersonId, PositionSides.Receivable, d.Amount)
                : new LedgerPosition(d.CreditorPersonId, PositionSides.Payable, -d.Amount))
            .ToList();
    }

    /// <summary>Inside a group every position is against the group as a whole (see <see cref="LedgerLegs.ForGroupSplit"/>).</summary>
    private static IReadOnlyList<LedgerPosition> GroupPositions(DebtOperation operation, Guid groupId, Guid personId)
    {
        if (operation.Kind == OperationKind.Settlement)
        {
            if (operation.FromPersonId == personId)
                return [new LedgerPosition(groupId, PositionSides.Payable, operation.Total)];

            if (operation.ToPersonId == personId)
                return [new LedgerPosition(groupId, PositionSides.Receivable, -operation.Total)];

            return [];
        }

        var net = operation.Payers.Where(p => p.PersonId == personId).Sum(p => p.Amount)
                  - operation.Shares.Where(s => s.PersonId == personId).Sum(s => s.Amount);

        return net == 0
            ? []
            : [new LedgerPosition(groupId, net > 0 ? PositionSides.Receivable : PositionSides.Payable, net)];
    }
}
