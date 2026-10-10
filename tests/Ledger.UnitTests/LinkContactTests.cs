using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Contacts.Domain;
using Ledger.Modules.Debts.Application.Links;
using Ledger.Modules.Debts.Domain;
using Ledger.Modules.Transactions.Contracts;

namespace Ledger.UnitTests;

/// <summary>Linking a contact that is already in use to a registered user.</summary>
public class LinkContactTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Pedro = Guid.NewGuid(); // Pedro's person id while he had no account
    private static readonly Guid PedroUser = Guid.NewGuid(); // his account
    private static readonly Guid Carl = Guid.NewGuid();
    private static readonly DateTimeOffset When = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static Contact UsedContact()
    {
        var contact = Contact.Create(Alice, "Pedro", null, null);
        contact.MarkInUse();
        return contact;
    }

    [Fact]
    public void A_used_contact_needs_a_link_request_and_only_one_at_a_time()
    {
        var contact = UsedContact();

        Assert.Equal("contact.in_use", Assert.Throws<DomainException>(() => contact.LinkTo(PedroUser, "p@x")).Code);

        var request = contact.RequestLink(PedroUser);

        Assert.Equal(LinkRequestStatus.Pending, request.Status);
        Assert.Equal(contact.PersonId, request.PersonId);
        Assert.Equal(request.Id, contact.PendingLinkRequestId);
        Assert.Equal("contact.link_pending", Assert.Throws<DomainException>(() => contact.RequestLink(PedroUser)).Code);
    }

    [Fact]
    public void An_unused_contact_is_linked_directly()
    {
        var contact = Contact.Create(Alice, "Pedro", null, null);

        Assert.Equal("contact.not_in_use", Assert.Throws<DomainException>(() => contact.RequestLink(PedroUser)).Code);

        contact.LinkTo(PedroUser, "p@x");
        Assert.Equal(PedroUser, contact.PersonId);
    }

    [Fact]
    public void Only_the_target_answers_only_the_owner_cancels_and_only_once()
    {
        var contact = UsedContact();
        var request = contact.RequestLink(PedroUser);

        Assert.Equal("link.not_for_you", Assert.Throws<DomainException>(() => request.Accept(Alice)).Code);
        Assert.Equal("link.not_yours", Assert.Throws<DomainException>(() => request.Cancel(PedroUser)).Code);

        request.Accept(PedroUser);
        Assert.Equal(LinkRequestStatus.Applying, request.Status);
        Assert.Equal("link.answered", Assert.Throws<DomainException>(() => request.Reject(PedroUser)).Code);

        Assert.True(request.MarkApplied());
        Assert.False(request.MarkApplied());
        Assert.False(request.MarkFailed("late"));
    }

    [Fact]
    public void A_failed_merge_keeps_the_reason_and_frees_the_contact()
    {
        var contact = UsedContact();
        var request = contact.RequestLink(PedroUser);
        request.Accept(PedroUser);

        Assert.True(request.MarkFailed("Your books are closed up to 2026-05-01."));
        contact.ClearLinkRequest(request.Id);

        Assert.Equal(LinkRequestStatus.Failed, request.Status);
        Assert.Null(contact.PendingLinkRequestId);
        contact.RequestLink(PedroUser); // can try again
    }

    [Fact]
    public void Applying_the_merge_turns_the_contact_into_the_user_or_folds_it_into_the_existing_one()
    {
        var contact = UsedContact();
        contact.RequestLink(PedroUser);

        contact.CompleteMerge(PedroUser, "p@x");
        Assert.Equal(PedroUser, contact.PersonId);
        Assert.Equal(PedroUser, contact.LinkedUserId);
        Assert.Null(contact.PendingLinkRequestId);

        var old = UsedContact();
        var previousPersonId = old.PersonId;
        var survivor = Contact.Create(Alice, "Pedro (account)", "p@x", PedroUser);

        old.MergeInto(survivor);

        Assert.True(old.IsMerged);
        Assert.Equal(survivor.Id, old.MergedIntoContactId);
        Assert.Equal(previousPersonId, old.PersonId); // kept: person ids are unique per owner
        Assert.True(survivor.IsInUse);
        Assert.Equal("contact.merged", Assert.Throws<DomainException>(() => old.RequestLink(PedroUser)).Code);
    }

    [Fact]
    public void Copies_hold_positions_against_opening_balances_and_settled_pairs_cancel_out()
    {
        var loan = Completed(DebtOperation.Loan(Alice, Pedro, Guid.NewGuid(), 50, Currency.EUR, When, "Loan"));
        var loanDebt = Open(Debt.Create(loan.Id, null, Alice, Pedro, 50, Currency.EUR));

        var dinner = Completed(DebtOperation.Split(
            Alice, null, 90, Currency.EUR, SplitMethod.Equal, null,
            [new OperationPayer(Alice, 60, Guid.NewGuid()), new OperationPayer(Pedro, 30, null)],
            [(Alice, 30m), (Pedro, 30m), (Carl, 30m)], When.AddDays(1), "Dinner"));
        // Carl owes Alice 30; Pedro is even.
        var dinnerDebt = Open(Debt.Create(dinner.Id, null, Alice, Carl, 30, Currency.EUR));

        var settlement = Completed(DebtOperation.Settlement(
            Alice, SettlementScope.Debt, null, Pedro, Alice, 50, Currency.EUR, null, When.AddDays(2), null));
        loanDebt.StartSettlement(settlement.Id);
        loanDebt.CompleteSettlement(settlement.Id);

        var copies = MergeCopies.Build([settlement, dinner, loan], [loanDebt, dinnerDebt], Pedro, PedroUser);

        // The dinner did not leave Pedro owing or owed anything: nothing to copy.
        Assert.Equal([loan.Id, settlement.Id], copies.Select(c => c.Operation.Id));
        Assert.All(copies, c =>
        {
            Assert.Equal(PedroUser, c.Leg.UserId);
            Assert.Null(c.Leg.WalletId);
            Assert.Equal(0, c.Leg.CashAmount);
            Assert.Equal(0, c.Leg.ExpenseAmount);
            Assert.Equal(0, c.Leg.OpeningBalanceAmount + c.Leg.Positions.Sum(p => p.Amount));
        });

        var loanCopy = copies[0].Leg.Positions.Single();
        Assert.Equal((Alice, PositionSides.Payable, -50m), (loanCopy.CounterpartyId, loanCopy.Side, loanCopy.Amount));

        var settlementCopy = copies[1].Leg.Positions.Single();
        Assert.Equal((Alice, PositionSides.Payable, 50m), (settlementCopy.CounterpartyId, settlementCopy.Side, settlementCopy.Amount));
    }

    [Fact]
    public void Group_copies_are_against_the_group()
    {
        var group = Guid.NewGuid();
        var expense = Completed(DebtOperation.Split(
            Alice, group, 100, Currency.EUR, SplitMethod.Equal, null,
            [new OperationPayer(Alice, 100, Guid.NewGuid())],
            [(Alice, 50m), (Pedro, 50m)], When, "Hotel"));
        var payment = Completed(DebtOperation.Settlement(
            Alice, SettlementScope.GroupPair, group, Pedro, Alice, 20, Currency.EUR, null, When.AddDays(1), null));

        var copies = MergeCopies.Build([expense, payment], [], Pedro, PedroUser);

        Assert.Equal(-50m, copies[0].Leg.Positions.Single(p => p.CounterpartyId == group).Amount);
        Assert.Equal(PositionSides.Payable, copies[0].Leg.Positions.Single().Side);
        Assert.Equal(20m, copies[1].Leg.Positions.Single(p => p.CounterpartyId == group).Amount);
        Assert.Equal(30m, copies.Sum(c => c.Leg.OpeningBalanceAmount)); // still owes the group 30
    }

    private static DebtOperation Completed(DebtOperation operation)
    {
        operation.Complete();
        return operation;
    }

    private static Debt Open(Debt debt)
    {
        debt.Activate();
        return debt;
    }
}
