using Ledger.Modules.Debts.Domain;
using Ledger.Modules.Transactions.Contracts;

namespace Ledger.Modules.Debts.Application.Operations;

/// <summary>
/// Translates debt operations into their effect on the books of every registered user involved
/// (the ledger contract). Non-registered people have no books: their debts live only here.
/// </summary>
internal static class LedgerLegs
{
    public static IReadOnlyList<LedgerLeg> ForLoan(DebtOperation loan, PersonInfo lender, PersonInfo borrower)
    {
        var amount = loan.Total;
        var legs = new List<LedgerLeg>
        {
            new(lender.UserId!.Value, loan.Payers.Single().WalletId, null, -amount, 0,
                [new LedgerPosition(borrower.PersonId, PositionSides.Receivable, amount)])
        };

        if (borrower.UserId is { } borrowerUserId)
        {
            legs.Add(new LedgerLeg(borrowerUserId, null, null, amount, 0,
                [new LedgerPosition(lender.PersonId, PositionSides.Payable, -amount)]));
        }

        return legs;
    }

    /// <summary>
    /// A debt that already existed: recorded as an expense for the debtor and as income for the
    /// creditor, with no money moving (it is paid later with a settlement).
    /// </summary>
    public static IReadOnlyList<LedgerLeg> ForInitialDebt(DebtOperation debt, PersonInfo creditor, PersonInfo debtor)
    {
        var amount = debt.Total;
        var legs = new List<LedgerLeg>();

        if (debtor.UserId is { } debtorUserId)
        {
            legs.Add(new LedgerLeg(
                debtorUserId, null, debtorUserId == debt.InitiatorUserId ? debt.CategoryId : null, 0, amount,
                [new LedgerPosition(creditor.PersonId, PositionSides.Payable, -amount)]));
        }

        if (creditor.UserId is { } creditorUserId)
        {
            legs.Add(new LedgerLeg(
                creditorUserId, null, creditorUserId == debt.InitiatorUserId ? debt.CategoryId : null, 0, 0,
                [new LedgerPosition(debtor.PersonId, PositionSides.Receivable, amount)],
                IncomeAmount: amount));
        }

        return legs;
    }

    /// <summary>
    /// Split outside groups: each registered user records what they paid, what they consumed and
    /// their debts with every other person.
    /// </summary>
    public static IReadOnlyList<LedgerLeg> ForSplit(
        DebtOperation split,
        IReadOnlyList<Debt> debts,
        IReadOnlyDictionary<Guid, PersonInfo> people)
    {
        var legs = new List<LedgerLeg>();

        foreach (var person in Involved(split).Select(id => people[id]).Where(p => p.UserId is not null))
        {
            var positions = debts
                .Where(d => d.Involves(person.PersonId))
                .Select(d => d.CreditorPersonId == person.PersonId
                    ? new LedgerPosition(d.DebtorPersonId, PositionSides.Receivable, d.Amount)
                    : new LedgerPosition(d.CreditorPersonId, PositionSides.Payable, -d.Amount))
                .ToList();

            AddSplitLeg(legs, split, person, positions);
        }

        return legs;
    }

    /// <summary>Split inside a group: each registered member owes to (or is owed by) the group as a whole.</summary>
    public static IReadOnlyList<LedgerLeg> ForGroupSplit(
        DebtOperation split,
        IReadOnlyDictionary<Guid, PersonInfo> members)
    {
        var legs = new List<LedgerLeg>();

        foreach (var person in Involved(split).Select(id => members[id]).Where(p => p.UserId is not null))
        {
            var net = Paid(split, person.PersonId) - Consumed(split, person.PersonId);
            List<LedgerPosition> positions = net == 0
                ? []
                : [new LedgerPosition(split.GroupId!.Value, net > 0 ? PositionSides.Receivable : PositionSides.Payable, net)];

            AddSplitLeg(legs, split, person, positions);
        }

        return legs;
    }

    /// <summary>Settling debts between two people: debts disappear and money changes hands.</summary>
    public static IReadOnlyList<LedgerLeg> ForDebtSettlement(
        DebtOperation settlement,
        IReadOnlyList<Debt> debts,
        PersonInfo first,
        PersonInfo second,
        Guid initiatorUserId,
        Guid? walletId)
    {
        var legs = new List<LedgerLeg>();

        foreach (var (person, other) in new[] { (first, second), (second, first) })
        {
            if (person.UserId is not { } userId)
                continue;

            var owedToPerson = debts.Where(d => d.CreditorPersonId == person.PersonId).Sum(d => d.Amount);
            var owedByPerson = debts.Where(d => d.DebtorPersonId == person.PersonId).Sum(d => d.Amount);

            var positions = new List<LedgerPosition>();

            if (owedToPerson != 0)
                positions.Add(new LedgerPosition(other.PersonId, PositionSides.Receivable, -owedToPerson));

            if (owedByPerson != 0)
                positions.Add(new LedgerPosition(other.PersonId, PositionSides.Payable, owedByPerson));

            legs.Add(new LedgerLeg(
                userId,
                userId == initiatorUserId ? walletId : null,
                null,
                owedToPerson - owedByPerson,
                0,
                positions));
        }

        return legs;
    }

    /// <summary>One member pays another the amount suggested by the group's simplified balances.</summary>
    public static IReadOnlyList<LedgerLeg> ForGroupSettlement(
        DebtOperation settlement,
        PersonInfo payer,
        PersonInfo payee,
        Guid initiatorUserId,
        Guid? walletId)
    {
        var groupId = settlement.GroupId!.Value;
        var amount = settlement.Total;
        var legs = new List<LedgerLeg>();

        if (payer.UserId is { } payerUserId)
        {
            legs.Add(new LedgerLeg(payerUserId, payerUserId == initiatorUserId ? walletId : null, null, -amount, 0,
                [new LedgerPosition(groupId, PositionSides.Payable, amount)]));
        }

        if (payee.UserId is { } payeeUserId)
        {
            legs.Add(new LedgerLeg(payeeUserId, payeeUserId == initiatorUserId ? walletId : null, null, amount, 0,
                [new LedgerPosition(groupId, PositionSides.Receivable, -amount)]));
        }

        return legs;
    }

    private static void AddSplitLeg(
        List<LedgerLeg> legs,
        DebtOperation split,
        PersonInfo person,
        IReadOnlyList<LedgerPosition> positions)
    {
        var isInitiator = person.UserId == split.InitiatorUserId;
        var paid = Paid(split, person.PersonId);
        var consumed = Consumed(split, person.PersonId);

        if (paid == 0 && consumed == 0 && positions.Count == 0)
            return;

        legs.Add(new LedgerLeg(
            person.UserId!.Value,
            isInitiator ? split.Payers.SingleOrDefault(p => p.PersonId == person.PersonId)?.WalletId : null,
            isInitiator ? split.CategoryId : null,
            -paid,
            consumed,
            positions));
    }

    public static IEnumerable<Guid> Involved(DebtOperation operation)
        => operation.Payers.Select(p => p.PersonId)
            .Concat(operation.Shares.Select(s => s.PersonId))
            .Distinct();

    private static decimal Paid(DebtOperation split, Guid personId)
        => split.Payers.Where(p => p.PersonId == personId).Sum(p => p.Amount);

    private static decimal Consumed(DebtOperation split, Guid personId)
        => split.Shares.Where(s => s.PersonId == personId).Sum(s => s.Amount);
}
