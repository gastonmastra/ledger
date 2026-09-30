using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Domain;

namespace Ledger.UnitTests;

public class SplitMathTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carl = Guid.NewGuid();

    [Fact]
    public void Equal_split_gives_leftover_cents_to_the_first_participants()
    {
        var shares = SplitMath.ComputeShares(100, SplitMethod.Equal, [new(Ana), new(Bob), new(Carl)]);

        Assert.Equal([33.34m, 33.33m, 33.33m], shares.Select(s => s.Amount));
    }

    [Fact]
    public void Percent_split_adds_up_to_the_total()
    {
        var shares = SplitMath.ComputeShares(10, SplitMethod.Percent, [new(Ana, 33.3m), new(Bob, 33.3m), new(Carl, 33.4m)]);

        Assert.Equal(10, shares.Sum(s => s.Amount));
        Assert.Throws<DomainException>(() =>
            SplitMath.ComputeShares(10, SplitMethod.Percent, [new(Ana, 50), new(Bob, 40)]));
    }

    [Fact]
    public void Custom_split_must_match_the_total()
    {
        var shares = SplitMath.ComputeShares(30, SplitMethod.Custom, [new(Ana, 10), new(Bob, 20)]);

        Assert.Equal([10m, 20m], shares.Select(s => s.Amount));
        Assert.Equal("split.amount_total", Assert.Throws<DomainException>(() =>
            SplitMath.ComputeShares(30, SplitMethod.Custom, [new(Ana, 10), new(Bob, 10)])).Code);
    }

    [Fact]
    public void Single_payer_is_owed_every_other_share()
    {
        var debts = SplitMath.AllocateDebts(
            [(Ana, 90m)],
            [(Ana, 30m), (Bob, 30m), (Carl, 30m)]);

        Assert.Equal(2, debts.Count);
        Assert.All(debts, d => Assert.Equal(Ana, d.CreditorPersonId));
        Assert.All(debts, d => Assert.Equal(30, d.Amount));
    }

    [Fact]
    public void Several_payers_share_the_debts_exactly_to_the_cent()
    {
        // 100 split in three; Ana paid 60, Bob paid 40.
        var paid = new List<(Guid, decimal)> { (Ana, 60m), (Bob, 40m) };
        var shares = SplitMath.ComputeShares(100, SplitMethod.Equal, [new(Ana), new(Bob), new(Carl)]);

        var debts = SplitMath.AllocateDebts(paid, shares);
        var nets = SplitMath.NetPositions(paid, shares).ToDictionary(n => n.PersonId, n => n.Cents / 100m);

        foreach (var (person, net) in nets)
        {
            var owedToPerson = debts.Where(d => d.CreditorPersonId == person).Sum(d => d.Amount);
            var owedByPerson = debts.Where(d => d.DebtorPersonId == person).Sum(d => d.Amount);
            Assert.Equal(net, owedToPerson - owedByPerson);
        }

        Assert.All(debts, d => Assert.Equal(Carl, d.DebtorPersonId));
    }

    [Fact]
    public void Payers_must_cover_the_shares()
        => Assert.Throws<DomainException>(() => SplitMath.AllocateDebts([(Ana, 10m)], [(Ana, 5m), (Bob, 6m)]));

    [Fact]
    public void Simplify_removes_intermediaries()
    {
        // Ana owes Bob 10 and Bob owes Carl 10: Ana can pay Carl directly.
        var payments = SplitMath.Simplify(new Dictionary<Guid, decimal> { [Ana] = -10, [Bob] = 0, [Carl] = 10 });

        var payment = Assert.Single(payments);
        Assert.Equal((Ana, Carl, 10m), (payment.FromPersonId, payment.ToPersonId, payment.Amount));
    }

    [Fact]
    public void Simplify_settles_every_balance()
    {
        var balances = new Dictionary<Guid, decimal> { [Ana] = 45.5m, [Bob] = -20.25m, [Carl] = -25.25m };

        var payments = SplitMath.Simplify(balances);

        foreach (var (person, balance) in balances)
        {
            var received = payments.Where(p => p.ToPersonId == person).Sum(p => p.Amount);
            var paid = payments.Where(p => p.FromPersonId == person).Sum(p => p.Amount);
            Assert.Equal(balance, received - paid);
        }

        Assert.True(payments.Count <= balances.Count - 1);
    }
}
