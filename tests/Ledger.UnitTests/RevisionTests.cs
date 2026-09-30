using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Domain;
using Ledger.Modules.Transactions.Domain;

namespace Ledger.UnitTests;

public class RevisionTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly AccountId Bank = new(Guid.NewGuid());
    private static readonly AccountId Food = new(Guid.NewGuid());

    private static Transaction Expense(decimal amount, DateTimeOffset? when = null)
        => TransactionBuilder
            .Create(Owner, when ?? DateTimeOffset.UtcNow.AddDays(-1), TransactionKind.Expense, "Lunch")
            .Add(Food, amount, Currency.EUR)
            .Add(Bank, -amount, Currency.EUR)
            .Build();

    [Fact]
    public void A_reversal_cancels_the_original_on_the_same_date()
    {
        var original = Expense(20);

        var reversal = Transaction.ReversalOf(original, Owner, ChangeAction.Delete, "Duplicated");

        Assert.Equal(TransactionKind.Reversal, reversal.Kind);
        Assert.Equal(original.OccurredAt, reversal.OccurredAt);
        Assert.Equal(original.EntryId, reversal.EntryId);
        Assert.Equal(original.Id, reversal.ReversalOfId);
        Assert.Equal(reversal.Id, original.ReversedById);
        Assert.False(original.IsCurrent);
        Assert.False(reversal.IsCurrent);

        foreach (var posting in original.Postings)
            Assert.Contains(reversal.Postings, p => p.AccountId == posting.AccountId && p.Amount == -posting.Amount);
    }

    [Fact]
    public void A_transaction_can_be_reversed_only_once()
    {
        var original = Expense(20);
        Transaction.ReversalOf(original, Owner, ChangeAction.Delete, "Duplicated");

        var error = Assert.Throws<DomainException>(() => Transaction.ReversalOf(original, Owner, ChangeAction.Delete, "Again"));
        Assert.Equal("transaction.not_current", error.Code);
    }

    [Fact]
    public void Every_change_needs_a_reason()
        => Assert.Throws<DomainException>(() => Transaction.ReversalOf(Expense(20), Owner, ChangeAction.Edit, " "));

    [Fact]
    public void A_correction_is_the_next_revision_of_the_same_entry()
    {
        var original = Expense(20);
        Transaction.ReversalOf(original, Owner, ChangeAction.Edit, "Typo");

        var corrected = Expense(25);
        corrected.BecomeRevisionOf(original, original.Revision + 1, Owner, ChangeAction.Edit, "Typo");

        Assert.Equal(original.EntryId, corrected.EntryId);
        Assert.Equal(2, corrected.Revision);
        Assert.True(corrected.IsCurrent);
        Assert.Equal("Typo", corrected.ChangeReason);
    }

    [Fact]
    public void The_books_lock_covers_the_lock_date_and_before()
    {
        var until = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);
        var books = BooksLock.For(Owner);
        books.Set(until);

        Assert.True(books.Covers(until));
        Assert.True(books.Covers(until.AddDays(-40)));
        Assert.False(books.Covers(until.AddSeconds(1)));
        Assert.Equal("books.locked", Assert.Throws<DomainException>(() => books.EnsureOpen(until)).Code);

        books.Set(null);
        Assert.False(books.Covers(until));
    }

    [Fact]
    public void A_change_nobody_has_to_confirm_is_answered_at_once()
    {
        var author = Guid.NewGuid();
        var change = OperationChange.Request(Guid.NewGuid(), ChangeKind.Delete, author, "Duplicated", null, null, [author]);

        Assert.Empty(change.Approvals);
        Assert.True(change.AllAnswered);
        Assert.False(change.AnyRejected);
    }

    [Fact]
    public void Each_linked_user_answers_for_themselves()
    {
        var author = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var carl = Guid.NewGuid();
        var change = OperationChange.Request(Guid.NewGuid(), ChangeKind.Revise, author, "Wrong amount", "{}", "{}", [author, bob, carl]);

        Assert.Equal(2, change.Approvals.Count);
        Assert.Equal(ChangeStatus.Pending, change.Status);

        change.Respond(bob, accept: true, null);
        Assert.False(change.AllAnswered);

        change.Respond(carl, accept: false, "It was 40");
        Assert.True(change.AllAnswered);
        Assert.True(change.AnyRejected);
        Assert.Equal([bob], change.AcceptedUserIds);
    }

    [Fact]
    public void Only_the_people_asked_can_answer_and_only_once()
    {
        var author = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var change = OperationChange.Request(Guid.NewGuid(), ChangeKind.Delete, author, "Duplicated", null, null, [author, bob]);

        Assert.Equal("change.not_approver", Assert.Throws<DomainException>(() => change.Respond(author, true, null)).Code);

        change.Respond(bob, true, null);
        Assert.Equal("change.already_answered", Assert.Throws<DomainException>(() => change.Respond(bob, false, null)).Code);

        change.StartApplying(null);
        Assert.Equal("change.not_pending", Assert.Throws<DomainException>(() => change.Respond(bob, true, null)).Code);
    }

    [Fact]
    public void An_operation_can_have_one_pending_change_at_a_time()
    {
        var me = Guid.NewGuid();
        var loan = DebtOperation.Loan(me, Guid.NewGuid(), Guid.NewGuid(), 10, Currency.EUR, DateTimeOffset.UtcNow, null);

        // Not recorded by the ledger yet.
        Assert.Equal("operation.not_current", Assert.Throws<DomainException>(() => loan.StartChange(Guid.NewGuid())).Code);

        loan.Complete();
        loan.StartChange(Guid.NewGuid());
        Assert.Equal("operation.change_pending", Assert.Throws<DomainException>(() => loan.StartChange(Guid.NewGuid())).Code);

        loan.MarkDeleted();
        loan.StartChange(Guid.NewGuid(), restoring: true);
    }

    [Fact]
    public void Undoing_a_settlement_reopens_its_debts()
    {
        var settlementId = Guid.NewGuid();
        var debt = Debt.Create(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), 30, Currency.EUR);
        debt.Activate();
        debt.StartSettlement(settlementId);
        debt.CompleteSettlement(settlementId);
        Assert.True(debt.IsPaid);

        debt.Reopen(settlementId);

        Assert.Equal(DebtStatus.Open, debt.Status);
        Assert.False(debt.IsPaid);
        Assert.Null(debt.SettledAt);
    }
}
