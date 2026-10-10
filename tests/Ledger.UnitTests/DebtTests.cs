using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Application;
using Ledger.Modules.Debts.Application.Operations;
using Ledger.Modules.Debts.Domain;

namespace Ledger.UnitTests;

public class DebtTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carl = Guid.NewGuid();

    [Fact]
    public void A_debt_is_settled_only_once()
    {
        var debt = Debt.Create(Guid.NewGuid(), null, Me, Bob, 10, Currency.EUR);
        Assert.Throws<DomainException>(() => debt.StartSettlement(Guid.NewGuid()));

        debt.Activate();
        var settlement = Guid.NewGuid();
        debt.StartSettlement(settlement);

        Assert.True(debt.IsSettling);
        Assert.Throws<DomainException>(() => debt.StartSettlement(Guid.NewGuid()));

        debt.CancelSettlement(settlement);
        Assert.False(debt.IsSettling);
    }

    [Fact]
    public void Nobody_can_lend_to_themselves()
        => Assert.Throws<DomainException>(() =>
            DebtOperation.Loan(Me, Me, Guid.NewGuid(), 10, Currency.EUR, DateTimeOffset.UtcNow, null));

    [Fact]
    public void Every_ledger_leg_of_a_split_balances()
    {
        var people = new Dictionary<Guid, PersonInfo>
        {
            [Me] = new(Me, "Me", Me),
            [Bob] = new(Bob, "Bob", Bob),
            [Carl] = new(Carl, "Carl", null)
        };
        var shares = SplitMath.ComputeShares(100, SplitMethod.Equal, [new(Me), new(Bob), new(Carl)]);
        var payers = new List<OperationPayer> { new(Me, 70, Guid.NewGuid()), new(Bob, 30, null) };

        var split = DebtOperation.Split(Me, null, 100, Currency.EUR, SplitMethod.Equal, null, payers, shares,
            DateTimeOffset.UtcNow, "Dinner");
        var debts = SplitMath.AllocateDebts(payers.Select(p => (p.PersonId, p.Amount)).ToList(), shares)
            .Select(d => Debt.Create(split.Id, null, d.CreditorPersonId, d.DebtorPersonId, d.Amount, Currency.EUR))
            .ToList();

        var legs = LedgerLegs.ForSplit(split, debts, people);

        // Carl is not a registered user: only me and Bob have books.
        Assert.Equal([Me, Bob], legs.Select(l => l.UserId));
        Assert.All(legs, leg => Assert.Equal(0, leg.CashAmount + leg.ExpenseAmount + leg.Positions.Sum(p => p.Amount)));

        // Only my own payment uses one of my wallets; Bob will choose his later.
        Assert.NotNull(legs[0].WalletId);
        Assert.Null(legs[1].WalletId);
    }

    [Fact]
    public void Every_ledger_leg_of_a_group_split_balances_against_the_group()
    {
        var groupId = Guid.NewGuid();
        var members = new Dictionary<Guid, PersonInfo>
        {
            [Me] = new(Me, "Me", Me),
            [Bob] = new(Bob, "Bob", Bob),
            [Carl] = new(Carl, "Carl", null)
        };
        var shares = SplitMath.ComputeShares(90, SplitMethod.Equal, [new(Me), new(Bob), new(Carl)]);
        var split = DebtOperation.Split(Me, groupId, 90, Currency.EUR, SplitMethod.Equal, null,
            [new(Me, 90, Guid.NewGuid())], shares, DateTimeOffset.UtcNow, "Hotel");

        var legs = LedgerLegs.ForGroupSplit(split, members);

        Assert.All(legs, leg => Assert.Equal(0, leg.CashAmount + leg.ExpenseAmount + leg.Positions.Sum(p => p.Amount)));
        Assert.All(legs.SelectMany(l => l.Positions), p => Assert.Equal(groupId, p.CounterpartyId));
    }
}

public class InitialDebtTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Sofi = Guid.NewGuid();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_initial_debt_is_neither_income_nor_expense(bool iOwe)
    {
        var me = new PersonInfo(Me, "Me", Me);
        var sofi = new PersonInfo(Sofi, "Sofi", Sofi); // linked user: mirrored
        var (creditor, debtor) = iOwe ? (sofi, me) : (me, sofi);

        var debt = DebtOperation.InitialDebt(
            Me, creditor.PersonId, debtor.PersonId, 50, Currency.EUR, DateTimeOffset.UtcNow, null);

        var legs = LedgerLegs.ForInitialDebt(debt, creditor, debtor);

        Assert.Equal(2, legs.Count);
        Assert.All(legs, leg =>
            Assert.Equal(0, leg.CashAmount + leg.ExpenseAmount - leg.IncomeAmount + leg.OpeningBalanceAmount + leg.Positions.Sum(p => p.Amount)));
        Assert.All(legs, leg => Assert.Null(leg.WalletId)); // no money moves
        Assert.All(legs, leg => Assert.Equal(0, leg.ExpenseAmount));
        Assert.All(legs, leg => Assert.Equal(0, leg.IncomeAmount));

        // The debt's balance comes from Opening balances, like a wallet's starting amount.
        Assert.Equal(50, legs.Single(l => l.UserId == debtor.UserId).OpeningBalanceAmount);
        Assert.Equal(-50, legs.Single(l => l.UserId == creditor.UserId).OpeningBalanceAmount);
    }

    [Fact]
    public void Only_registered_people_get_books()
    {
        var me = new PersonInfo(Me, "Me", Me);
        var sofi = new PersonInfo(Sofi, "Sofi", null);

        var debt = DebtOperation.InitialDebt(Me, Sofi, Me, 10, Currency.EUR, DateTimeOffset.UtcNow, null);

        Assert.Single(LedgerLegs.ForInitialDebt(debt, sofi, me));
    }
}
