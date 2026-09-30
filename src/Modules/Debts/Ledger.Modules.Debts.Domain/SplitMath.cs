using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Debts.Domain;

public sealed record Participant(Guid PersonId, decimal? Value = null);

public sealed record PairDebt(Guid DebtorPersonId, Guid CreditorPersonId, decimal Amount);

public sealed record Payment(Guid FromPersonId, Guid ToPersonId, decimal Amount);

/// <summary>Money math of splits. Everything is computed in whole cents so totals always add up.</summary>
public static class SplitMath
{
    /// <summary>Computes how much of <paramref name="total"/> each participant consumed.</summary>
    /// <remarks>Rounding leftovers (cents) go to the first participants, one cent each.</remarks>
    public static IReadOnlyList<(Guid PersonId, decimal Amount)> ComputeShares(
        decimal total,
        SplitMethod method,
        IReadOnlyList<Participant> participants)
    {
        Money.EnsurePositive(total, "total");

        if (participants.Count == 0)
            throw new DomainException("split.no_participants", "A split needs at least one participant.");

        if (participants.Select(p => p.PersonId).Distinct().Count() != participants.Count)
            throw new DomainException("split.duplicate_participant", "Each participant can appear only once.");

        var totalCents = ToCents(total);

        long[] cents = method switch
        {
            SplitMethod.Equal => DistributeEvenly(totalCents, participants.Count),
            SplitMethod.Percent => ByPercent(totalCents, participants),
            SplitMethod.Custom => ByAmount(totalCents, participants),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };

        return participants
            .Select((p, i) => (p.PersonId, FromCents(cents[i])))
            .ToList();
    }

    /// <summary>
    /// Who owes whom after an expense with several payers. Every debtor (paid less than consumed)
    /// owes every creditor (paid more than consumed) proportionally to what the creditor advanced.
    /// Row and column totals are exact to the cent.
    /// </summary>
    public static IReadOnlyList<PairDebt> AllocateDebts(
        IReadOnlyList<(Guid PersonId, decimal Amount)> paid,
        IReadOnlyList<(Guid PersonId, decimal Amount)> shares)
    {
        if (paid.Sum(p => p.Amount) != shares.Sum(s => s.Amount))
            throw new DomainException("split.payers_mismatch", "What the payers paid must add up to the total.");

        var nets = NetPositions(paid, shares);
        var creditors = nets.Where(n => n.Cents > 0).ToList();
        var debtors = nets.Where(n => n.Cents < 0).Select(n => (n.PersonId, Cents: -n.Cents)).ToList();
        var creditTotal = creditors.Sum(c => c.Cents);

        var allocation = new long[debtors.Count, creditors.Count];
        var capacity = creditors.Select(c => c.Cents).ToArray();

        for (var d = 0; d < debtors.Count; d++)
        {
            var remaining = debtors[d].Cents;

            for (var c = 0; c < creditors.Count; c++)
            {
                var amount = debtors[d].Cents * creditors[c].Cents / creditTotal;
                allocation[d, c] = amount;
                capacity[c] -= amount;
                remaining -= amount;
            }

            // Floors leave a few cents; give them to creditors that still have room.
            for (var c = 0; remaining > 0 && c < creditors.Count; c++)
            {
                var amount = Math.Min(remaining, capacity[c]);
                allocation[d, c] += amount;
                capacity[c] -= amount;
                remaining -= amount;
            }
        }

        var result = new List<PairDebt>();

        for (var d = 0; d < debtors.Count; d++)
        for (var c = 0; c < creditors.Count; c++)
        {
            if (allocation[d, c] > 0)
                result.Add(new PairDebt(debtors[d].PersonId, creditors[c].PersonId, FromCents(allocation[d, c])));
        }

        return result;
    }

    /// <summary>What each person paid minus what they consumed (positive: others owe them).</summary>
    public static IReadOnlyList<(Guid PersonId, long Cents)> NetPositions(
        IReadOnlyList<(Guid PersonId, decimal Amount)> paid,
        IReadOnlyList<(Guid PersonId, decimal Amount)> shares)
    {
        var order = paid.Select(p => p.PersonId).Concat(shares.Select(s => s.PersonId)).Distinct().ToList();

        return order
            .Select(id => (id, ToCents(paid.Where(p => p.PersonId == id).Sum(p => p.Amount))
                                - ToCents(shares.Where(s => s.PersonId == id).Sum(s => s.Amount))))
            .ToList();
    }

    /// <summary>
    /// Minimum set of payments that brings every balance to zero (greedy: the biggest debtor pays
    /// the biggest creditor). Positive balance = the group owes the person.
    /// </summary>
    public static IReadOnlyList<Payment> Simplify(IReadOnlyDictionary<Guid, decimal> balances)
    {
        if (balances.Values.Sum() != 0)
            throw new DomainException("simplify.unbalanced", "Balances must add up to zero.");

        var creditors = balances.Where(b => b.Value > 0).ToDictionary(b => b.Key, b => ToCents(b.Value));
        var debtors = balances.Where(b => b.Value < 0).ToDictionary(b => b.Key, b => -ToCents(b.Value));
        var payments = new List<Payment>();

        while (creditors.Count > 0 && debtors.Count > 0)
        {
            var creditor = creditors.OrderByDescending(c => c.Value).ThenBy(c => c.Key).First();
            var debtor = debtors.OrderByDescending(d => d.Value).ThenBy(d => d.Key).First();
            var amount = Math.Min(creditor.Value, debtor.Value);

            payments.Add(new Payment(debtor.Key, creditor.Key, FromCents(amount)));

            Reduce(creditors, creditor.Key, amount);
            Reduce(debtors, debtor.Key, amount);
        }

        return payments;
    }

    private static void Reduce(Dictionary<Guid, long> balances, Guid key, long amount)
    {
        balances[key] -= amount;

        if (balances[key] == 0)
            balances.Remove(key);
    }

    private static long[] DistributeEvenly(long totalCents, int count)
    {
        var baseCents = totalCents / count;
        var leftover = totalCents % count;

        return Enumerable.Range(0, count)
            .Select(i => baseCents + (i < leftover ? 1 : 0))
            .ToArray();
    }

    private static long[] ByPercent(long totalCents, IReadOnlyList<Participant> participants)
    {
        if (participants.Any(p => p.Value is null or < 0))
            throw new DomainException("split.percent_required", "Every participant needs a percentage (0 or more).");

        if (participants.Sum(p => p.Value!.Value) != 100)
            throw new DomainException("split.percent_total", "Percentages must add up to 100.");

        var cents = participants
            .Select(p => (long)Math.Floor(totalCents * p.Value!.Value / 100))
            .ToArray();

        var leftover = totalCents - cents.Sum();

        for (var i = 0; leftover > 0; i = (i + 1) % cents.Length)
        {
            if (participants[i].Value == 0)
                continue;

            cents[i]++;
            leftover--;
        }

        return cents;
    }

    private static long[] ByAmount(long totalCents, IReadOnlyList<Participant> participants)
    {
        if (participants.Any(p => p.Value is null or < 0))
            throw new DomainException("split.amount_required", "Every participant needs an amount (0 or more).");

        foreach (var participant in participants)
            Money.EnsureCents(participant.Value!.Value, "share");

        var cents = participants.Select(p => ToCents(p.Value!.Value)).ToArray();

        if (cents.Sum() != totalCents)
            throw new DomainException("split.amount_total", "The shares must add up to the total.");

        return cents;
    }

    public static long ToCents(decimal amount) => (long)(amount * 100);

    public static decimal FromCents(long cents) => cents / 100m;
}
