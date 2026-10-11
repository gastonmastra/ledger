using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Application;
using Ledger.Modules.Debts.Application.Operations;
using Ledger.Modules.Debts.Domain;

namespace Ledger.UnitTests;

public class PaymentTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Brother = Guid.NewGuid();
    private static readonly DateTimeOffset Day = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static OpenDebt Owed(Guid creditor, Guid debtor, decimal available, int day, Guid? id = null)
        => new(id ?? Guid.NewGuid(), creditor, debtor, available, Day.AddDays(day), Day.AddDays(day));

    private static decimal PaidOn(AllocationResult result, OpenDebt debt)
        => result.Allocations.SingleOrDefault(a => a.DebtId == debt.DebtId)?.Amount ?? 0;

    [Fact]
    public void A_payment_pays_the_oldest_debt_first_and_leaves_the_next_partially_paid()
    {
        // My brother owes me 150 (oldest) and 50; he pays 170.
        var oldest = Owed(Me, Brother, 150, 1);
        var newest = Owed(Me, Brother, 50, 5);

        var result = PaymentAllocation.Allocate(Brother, Me, 170, [newest, oldest]);

        Assert.Equal(150, PaidOn(result, oldest));
        Assert.Equal(20, PaidOn(result, newest));
        Assert.Equal(0, result.Extra);
    }

    [Fact]
    public void An_edited_amount_is_applied_again_from_the_oldest_debt()
    {
        // The same payment edited to 130: the oldest is partially paid, the newest untouched.
        var oldest = Owed(Me, Brother, 150, 1);
        var newest = Owed(Me, Brother, 50, 5);

        var result = PaymentAllocation.Allocate(Brother, Me, 130, [oldest, newest]);

        Assert.Equal(130, PaidOn(result, oldest));
        Assert.Equal(0, PaidOn(result, newest));
        Assert.Single(result.Allocations);
    }

    [Fact]
    public void Paying_the_whole_balance_pays_every_debt()
    {
        var debts = new[] { Owed(Me, Brother, 150, 1), Owed(Me, Brother, 50, 5) };

        var result = PaymentAllocation.Allocate(Brother, Me, 200, debts);

        Assert.All(debts, d => Assert.Equal(d.Available, PaidOn(result, d)));
        Assert.Equal(0, result.Extra);
    }

    [Fact]
    public void Mutual_debts_cancel_out_before_the_money_pays_anything()
    {
        // He owes me 150 + 50, I owe him 30: the 30 cancels against his oldest, then 100 pays the rest of it.
        var his150 = Owed(Me, Brother, 150, 1);
        var his50 = Owed(Me, Brother, 50, 5);
        var mine30 = Owed(Brother, Me, 30, 3);

        var result = PaymentAllocation.Allocate(Brother, Me, 100, [his150, his50, mine30]);

        Assert.Equal(30, PaidOn(result, mine30));
        Assert.Equal(130, PaidOn(result, his150));
        Assert.Equal(0, PaidOn(result, his50));
        Assert.Equal(0, result.Extra);
    }

    [Fact]
    public void Paying_more_than_what_is_owed_leaves_the_extra_as_a_debt_the_other_way()
    {
        var debt = Owed(Me, Brother, 200, 1);

        var result = PaymentAllocation.Allocate(Brother, Me, 250, [debt]);

        Assert.Equal(200, PaidOn(result, debt));
        Assert.Equal(50, result.Extra);
    }

    [Fact]
    public void Without_debts_of_the_payer_the_whole_payment_is_extra()
    {
        // I owe him 30, he owes me 100; he pays me 20: the 30 cancel out, then his 20 pay more of his debt.
        var mine30 = Owed(Brother, Me, 30, 1);
        var his100 = Owed(Me, Brother, 100, 2);

        var result = PaymentAllocation.Allocate(Brother, Me, 20, [mine30, his100]);

        Assert.Equal(30, PaidOn(result, mine30));
        Assert.Equal(50, PaidOn(result, his100));
        Assert.Equal(0, result.Extra);

        // I only owe him: what he pays me is all extra (now I owe him that too).
        var onlyMine = PaymentAllocation.Allocate(Brother, Me, 20, [Owed(Brother, Me, 30, 1)]);
        Assert.Empty(onlyMine.Allocations);
        Assert.Equal(20, onlyMine.Extra);

        var noDebts = PaymentAllocation.Allocate(Brother, Me, 20, []);
        Assert.Empty(noDebts.Allocations);
        Assert.Equal(20, noDebts.Extra);
    }

    [Fact]
    public void Debts_of_the_same_moment_are_paid_in_the_order_they_were_created()
    {
        var first = new OpenDebt(Guid.NewGuid(), Me, Brother, 10, Day, Day);
        var second = new OpenDebt(Guid.NewGuid(), Me, Brother, 10, Day, Day.AddSeconds(1));

        var result = PaymentAllocation.Allocate(Brother, Me, 10, [second, first]);

        Assert.Equal(10, PaidOn(result, first));
        Assert.Equal(0, PaidOn(result, second));
    }

    [Fact]
    public void Debts_with_nothing_left_are_ignored()
    {
        var reserved = Owed(Me, Brother, 0, 1);
        var open = Owed(Me, Brother, 50, 2);

        var result = PaymentAllocation.Allocate(Brother, Me, 50, [reserved, open]);

        Assert.Equal(50, PaidOn(result, open));
        Assert.Single(result.Allocations);
    }

    [Theory]
    [InlineData(-1, "payment.amount_positive")]
    [InlineData(10.001, "money.too_many_decimals")]
    public void Invalid_amounts_are_refused(decimal amount, string code)
        => Assert.Equal(code, Assert.Throws<DomainException>(() => PaymentAllocation.Allocate(Brother, Me, amount, [])).Code);

    [Fact]
    public void Nobody_pays_themselves()
        => Assert.Equal("payment.self",
            Assert.Throws<DomainException>(() => PaymentAllocation.Allocate(Me, Me, 10, [])).Code);

    [Fact]
    public void A_debt_is_settled_once_fully_paid_and_reopens_when_a_payment_is_taken_back()
    {
        var debt = Debt.Create(Guid.NewGuid(), null, Me, Brother, 50, Currency.EUR);
        debt.Activate();

        debt.ApplyPayment(20);
        Assert.True(debt.IsPartiallyPaid);
        Assert.Equal(30, debt.Remaining);
        Assert.Equal(DebtStatus.Open, debt.Status);

        debt.ApplyPayment(30);
        Assert.Equal(DebtStatus.Settled, debt.Status);
        Assert.False(debt.IsPartiallyPaid);
        Assert.NotNull(debt.SettledAt);

        Assert.Equal("debt.not_open", Assert.Throws<DomainException>(() => debt.ApplyPayment(1)).Code);

        debt.RemovePayment(30);
        Assert.Equal(DebtStatus.Open, debt.Status);
        Assert.Equal(30, debt.Remaining);
        Assert.Null(debt.SettledAt);

        Assert.Equal("payment.too_much", Assert.Throws<DomainException>(() => debt.ApplyPayment(31)).Code);
    }

    [Theory]
    [InlineData(170, false)] // partial
    [InlineData(200, false)] // exact
    [InlineData(250, false)] // overpayment
    [InlineData(170, true)]  // with a mutual debt
    [InlineData(250, true)]
    public void Every_ledger_leg_of_a_payment_balances(decimal amount, bool mutual)
    {
        var me = new PersonInfo(Me, "Me", Me);
        var brother = new PersonInfo(Brother, "Brother", Brother);
        List<OpenDebt> debts = [Owed(Me, Brother, 150, 1), Owed(Me, Brother, 50, 5)];

        if (mutual)
            debts.Add(Owed(Brother, Me, 30, 3));

        var allocation = PaymentAllocation.Allocate(Brother, Me, amount, debts);
        var payment = DebtOperation.Settlement(
            Me, SettlementScope.Payment, null, Brother, Me, amount, Currency.EUR, Guid.NewGuid(), Day, null);

        var legs = LedgerLegs.ForPayment(payment, debts, allocation, brother, me, Me, Guid.NewGuid());

        Assert.Equal(2, legs.Count);
        Assert.All(legs, leg => Assert.Equal(0, leg.CashAmount + leg.Positions.Sum(p => p.Amount)));

        var mine = legs.Single(l => l.UserId == Me);
        Assert.Equal(amount, mine.CashAmount);
        Assert.NotNull(mine.WalletId);
        Assert.Equal(-amount, legs.Single(l => l.UserId == Brother).CashAmount);
        Assert.Null(legs.Single(l => l.UserId == Brother).WalletId);

        // What is left after the payment, from my side: what he owes me minus what I owe him.
        var owedToMeBefore = debts.Where(d => d.CreditorPersonId == Me).Sum(d => d.Available)
                             - debts.Where(d => d.CreditorPersonId == Brother).Sum(d => d.Available);
        Assert.Equal(owedToMeBefore - amount, owedToMeBefore + mine.Positions.Sum(p => p.Amount));
    }

    [Fact]
    public void Only_registered_people_get_a_leg()
    {
        var me = new PersonInfo(Me, "Me", Me);
        var carl = new PersonInfo(Guid.NewGuid(), "Carl", null);
        var debt = new OpenDebt(Guid.NewGuid(), Me, carl.PersonId, 40, Day, Day);
        var allocation = PaymentAllocation.Allocate(carl.PersonId, Me, 25, [debt]);
        var payment = DebtOperation.Settlement(
            Me, SettlementScope.Payment, null, carl.PersonId, Me, 25, Currency.EUR, Guid.NewGuid(), Day, null);

        var leg = Assert.Single(LedgerLegs.ForPayment(payment, [debt], allocation, carl, me, Me, Guid.NewGuid()));

        Assert.Equal(Me, leg.UserId);
        Assert.Equal(25, leg.CashAmount);
        Assert.Equal(-25, Assert.Single(leg.Positions).Amount);
    }
}
