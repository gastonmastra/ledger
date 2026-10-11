using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Debts.Domain;

/// <summary>An open debt between the two people of a payment, with what is still available to pay.</summary>
/// <param name="OccurredAt">When the operation that created it happened: older debts are paid first.</param>
public sealed record OpenDebt(
    Guid DebtId,
    Guid CreditorPersonId,
    Guid DebtorPersonId,
    decimal Available,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt);

public sealed record DebtAllocation(Guid DebtId, decimal Amount);

/// <param name="Allocations">How much of each debt the payment pays (offset debts included).</param>
/// <param name="Extra">Paid beyond what was owed: becomes a debt of the payee to the payer.</param>
public sealed record AllocationResult(IReadOnlyList<DebtAllocation> Allocations, decimal Extra);

/// <summary>
/// Applies a payment between two people to their debts (outside groups, one currency):
/// <list type="number">
/// <item>debts in both directions cancel out first, oldest with oldest;</item>
/// <item>the payment pays what the payer owes the payee, oldest first;</item>
/// <item>anything left over is <see cref="AllocationResult.Extra"/>: the payee now owes it to the payer.</item>
/// </list>
/// </summary>
public static class PaymentAllocation
{
    public static AllocationResult Allocate(
        Guid payerPersonId,
        Guid payeePersonId,
        decimal amount,
        IReadOnlyList<OpenDebt> openDebts)
    {
        if (payerPersonId == payeePersonId)
            throw new DomainException("payment.self", "A person cannot pay themselves.");

        if (amount < 0)
            throw new DomainException("payment.amount_positive", "The amount cannot be negative.");

        Money.EnsureCents(amount);

        static List<(OpenDebt Debt, decimal Left)> Queue(IEnumerable<OpenDebt> debts)
            => debts
                .Where(d => d.Available > 0)
                .OrderBy(d => d.OccurredAt)
                .ThenBy(d => d.CreatedAt)
                .ThenBy(d => d.DebtId)
                .Select(d => (d, d.Available))
                .ToList();

        // What the payer owes the payee (the payment pays these) and the other way round.
        var payerOwes = Queue(openDebts.Where(d => d.DebtorPersonId == payerPersonId && d.CreditorPersonId == payeePersonId));
        var payeeOwes = Queue(openDebts.Where(d => d.DebtorPersonId == payeePersonId && d.CreditorPersonId == payerPersonId));

        var allocated = new Dictionary<Guid, decimal>();

        void Allocate(Guid debtId, decimal value)
            => allocated[debtId] = allocated.GetValueOrDefault(debtId) + value;

        // 1. Net first: mutual debts cancel out, oldest with oldest.
        int i = 0, j = 0;
        while (i < payerOwes.Count && j < payeeOwes.Count)
        {
            var offset = Math.Min(payerOwes[i].Left, payeeOwes[j].Left);
            Allocate(payerOwes[i].Debt.DebtId, offset);
            Allocate(payeeOwes[j].Debt.DebtId, offset);
            payerOwes[i] = payerOwes[i] with { Left = payerOwes[i].Left - offset };
            payeeOwes[j] = payeeOwes[j] with { Left = payeeOwes[j].Left - offset };

            if (payerOwes[i].Left == 0)
                i++;

            if (payeeOwes[j].Left == 0)
                j++;
        }

        // 2. The money pays what the payer still owes, oldest first.
        var money = amount;
        for (; i < payerOwes.Count && money > 0; i++)
        {
            var paid = Math.Min(payerOwes[i].Left, money);
            Allocate(payerOwes[i].Debt.DebtId, paid);
            money -= paid;
        }

        // 3. Whatever is left was paid beyond what was owed.
        var order = openDebts.Select((d, index) => (d.DebtId, index)).ToDictionary(x => x.DebtId, x => x.index);

        return new AllocationResult(
            allocated
                .Where(a => a.Value > 0)
                .OrderBy(a => order[a.Key])
                .Select(a => new DebtAllocation(a.Key, a.Value))
                .ToList(),
            money);
    }
}
