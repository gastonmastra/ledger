using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Domain;
using Ledger.Modules.Transactions.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application.Operations;

/// <summary>An operation ready to be stored: its debts, its effect on each user's books and the people it involves.</summary>
internal sealed record BuiltOperation(
    DebtOperation Operation,
    IReadOnlyList<Debt> Debts,
    IReadOnlyList<LedgerLeg> Legs,
    IReadOnlyDictionary<Guid, PersonInfo> People)
{
    /// <summary>Registered users whose books the operation touches.</summary>
    public IReadOnlySet<Guid> RegisteredUserIds => LedgerLegs.RegisteredUsers(Operation, People);

    /// <summary>Payments only: how much of each debt it pays.</summary>
    public IReadOnlyList<DebtPayment> Payments { get; init; } = [];
}

/// <summary>
/// Builds loans, existing debts, splits and settlements, always from the point of view of the user
/// who first recorded them (the initiator): their contacts, wallets and categories. Used both to
/// record a new operation and to build a new version of an existing one.
/// </summary>
internal sealed class OperationFactory(IDebtsDbContext dbContext, PeopleDirectory people, PaymentBook payments)
{
    public async Task<BuiltOperation> LoanAsync(
        Guid initiatorUserId,
        PersonInfo borrower,
        Guid walletId,
        decimal amount,
        DateTimeOffset occurredAt,
        string? description,
        CancellationToken cancellationToken)
    {
        var me = await people.MeAsync(initiatorUserId, cancellationToken);
        var wallet = await WalletAsync(initiatorUserId, walletId, cancellationToken);

        var loan = DebtOperation.Loan(
            initiatorUserId, borrower.PersonId, wallet.WalletId, amount, wallet.Currency, occurredAt, description);

        var debt = Debt.Create(loan.Id, null, me.PersonId, borrower.PersonId, loan.Total, loan.Currency);

        return new BuiltOperation(
            loan, [debt], LedgerLegs.ForLoan(loan, me, borrower), People(me, borrower));
    }

    public async Task<BuiltOperation> InitialDebtAsync(
        Guid initiatorUserId,
        PersonInfo contact,
        InitialDebtDirection direction,
        decimal amount,
        Currency currency,
        DateTimeOffset occurredAt,
        string? description,
        CancellationToken cancellationToken)
    {
        var me = await people.MeAsync(initiatorUserId, cancellationToken);
        var iOwe = direction == InitialDebtDirection.IOwe;
        var creditor = iOwe ? contact : me;
        var debtor = iOwe ? me : contact;

        var operation = DebtOperation.InitialDebt(
            initiatorUserId,
            creditor.PersonId,
            debtor.PersonId,
            amount,
            currency,
            occurredAt,
            // Also shown in a linked contact's books, so the default text is neutral.
            description ?? "Debt from before Ledger");

        var debt = Debt.Create(operation.Id, null, creditor.PersonId, debtor.PersonId, operation.Total, operation.Currency);

        return new BuiltOperation(
            operation, [debt], LedgerLegs.ForInitialDebt(operation, creditor, debtor), People(me, contact));
    }

    /// <param name="requireMyWallet">
    /// False when somebody else edits the split: the initiator did not choose a wallet for a new
    /// payment of theirs, so it goes to their pending wallet assignments.
    /// </param>
    /// <param name="recordAs">Method to store instead of <paramref name="method"/> (restoring precomputed shares).</param>
    public async Task<BuiltOperation> SplitAsync(
        Guid initiatorUserId,
        Guid? groupId,
        decimal total,
        Currency currency,
        SplitMethod method,
        IReadOnlyList<SplitPayer> requestedPayers,
        IReadOnlyList<SplitParticipant> participants,
        Guid? categoryId,
        DateTimeOffset occurredAt,
        string? description,
        bool requireMyWallet,
        CancellationToken cancellationToken,
        SplitMethod? recordAs = null)
    {
        IReadOnlyDictionary<Guid, PersonInfo> allowed = groupId is { } g
            ? await GroupMembersAsync(g, cancellationToken)
            : await people.CircleAsync(initiatorUserId, cancellationToken);

        var involved = requestedPayers.Select(p => p.PersonId).Concat(participants.Select(p => p.PersonId));

        foreach (var personId in involved.Distinct())
        {
            if (!allowed.ContainsKey(personId))
                throw new DomainException(
                    "split.unknown_person",
                    groupId is null
                        ? $"Person '{personId}' is neither you nor one of your contacts."
                        : $"Person '{personId}' is not a member of the group.");
        }

        var payers = new List<OperationPayer>();

        foreach (var payer in requestedPayers)
        {
            Guid? walletId = null;

            if (payer.PersonId == initiatorUserId && (payer.WalletId is not null || requireMyWallet))
            {
                if (payer.WalletId is null)
                    throw new DomainException("split.wallet_required", "Choose the wallet you paid from.");

                var wallet = await WalletAsync(initiatorUserId, payer.WalletId.Value, cancellationToken);

                if (wallet.Currency != currency)
                    throw new DomainException(
                        "split.currency_mismatch",
                        $"The wallet holds {wallet.Currency} but the expense is in {currency}.");

                walletId = wallet.WalletId;
            }

            payers.Add(new OperationPayer(payer.PersonId, payer.Amount, walletId));
        }

        var shares = SplitMath.ComputeShares(
            total,
            method,
            participants.Select(p => new Participant(p.PersonId, p.Value)).ToList());

        var split = DebtOperation.Split(
            initiatorUserId, groupId, total, currency, recordAs ?? method, categoryId,
            payers, shares, occurredAt, description);

        var debts = SplitMath
            .AllocateDebts(payers.Select(p => (p.PersonId, p.Amount)).ToList(), shares)
            .Select(d => Debt.Create(split.Id, split.GroupId, d.CreditorPersonId, d.DebtorPersonId, d.Amount, currency))
            .ToList();

        if (debts.Count == 0)
            throw new DomainException(
                "split.no_debts",
                "Nobody owes anything in this split. Record it as a regular expense instead.");

        var legs = split.GroupId is null
            ? LedgerLegs.ForSplit(split, debts, allowed)
            : LedgerLegs.ForGroupSplit(split, allowed);

        return new BuiltOperation(split, debts, legs, allowed);
    }

    /// <summary>
    /// A payment between the initiator and <paramref name="other"/> outside groups: mutual debts cancel out, the money
    /// pays the payer's debts oldest first, and anything paid beyond that becomes a debt of the payee to the payer.
    /// </summary>
    /// <param name="replacing">The version being edited: what it paid is available again.</param>
    /// <param name="requireMyWallet">
    /// False when somebody else edits it and the initiator's wallet is unknown (settlements recorded before wallets
    /// were stored): the initiator's money goes to their pending wallet assignments.
    /// </param>
    public async Task<BuiltOperation> PaymentAsync(
        Guid initiatorUserId,
        PersonInfo other,
        bool initiatorPays,
        decimal amount,
        Currency currency,
        Guid? walletId,
        DateTimeOffset occurredAt,
        string? description,
        Guid? replacing,
        CancellationToken cancellationToken,
        bool requireMyWallet = true)
    {
        var me = await people.MeAsync(initiatorUserId, cancellationToken);
        var payer = initiatorPays ? me : other;
        var payee = initiatorPays ? other : me;

        var payable = await payments.PayableAsync(me.PersonId, other.PersonId, currency, replacing, cancellationToken);
        var allocation = PaymentAllocation.Allocate(payer.PersonId, payee.PersonId, amount, payable.Open);

        if (allocation.Allocations.Count == 0 && allocation.Extra == 0)
            throw new DomainException("payment.nothing", "There is nothing to pay: enter an amount.");

        var wallet = await RequireWalletIfMoneyMovesAsync(initiatorUserId, walletId, amount, currency, requireMyWallet, cancellationToken);

        var settlement = DebtOperation.Settlement(
            initiatorUserId,
            SettlementScope.Payment,
            null,
            payer.PersonId,
            payee.PersonId,
            amount,
            currency,
            wallet,
            occurredAt,
            // Also shown in a linked contact's books, so the default text names both sides.
            description ?? $"{payer.Name} paid {payee.Name}");

        var rows = allocation.Allocations
            .Select(a => DebtPayment.Create(settlement.Id, a.DebtId, a.Amount))
            .ToList();

        IReadOnlyList<Debt> extra = allocation.Extra > 0
            ? [Debt.ForExtraPayment(settlement.Id, payer.PersonId, payee.PersonId, allocation.Extra, currency)]
            : [];

        var legs = LedgerLegs.ForPayment(settlement, payable.Open, allocation, payer, payee, initiatorUserId, wallet);

        return new BuiltOperation(settlement, extra, legs, People(me, other)) { Payments = rows };
    }

    /// <summary>One group member pays another.</summary>
    public async Task<BuiltOperation> GroupSettlementAsync(
        Guid initiatorUserId,
        Guid groupId,
        PersonInfo from,
        PersonInfo to,
        decimal amount,
        Currency currency,
        Guid? walletId,
        DateTimeOffset occurredAt,
        string? description,
        CancellationToken cancellationToken,
        bool requireMyWallet = true)
    {
        var iMoveMoney = from.UserId == initiatorUserId || to.UserId == initiatorUserId;
        var wallet = iMoveMoney
            ? await RequireWalletIfMoneyMovesAsync(initiatorUserId, walletId, amount, currency, requireMyWallet, cancellationToken)
            : null;

        var settlement = DebtOperation.Settlement(
            initiatorUserId,
            SettlementScope.GroupPair,
            groupId,
            from.PersonId,
            to.PersonId,
            amount,
            currency,
            wallet,
            occurredAt,
            description);

        var members = await GroupMembersAsync(groupId, cancellationToken);

        return new BuiltOperation(
            settlement,
            [],
            LedgerLegs.ForGroupSettlement(settlement, from, to, initiatorUserId, wallet),
            members);
    }

    /// <summary>Everybody the operation may involve, as the initiator knows them.</summary>
    public async Task<IReadOnlyDictionary<Guid, PersonInfo>> PeopleOfAsync(
        DebtOperation operation,
        CancellationToken cancellationToken)
        => operation.GroupId is { } groupId
            ? await GroupMembersAsync(groupId, cancellationToken)
            : await people.CircleAsync(operation.InitiatorUserId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, PersonInfo>> GroupMembersAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var members = await dbContext.GroupMembers.AsNoTracking()
            .Where(x => x.GroupId == groupId)
            .ToListAsync(cancellationToken);

        if (members.Count == 0)
            throw new NotFoundException("Group", groupId);

        return members.ToDictionary(m => m.PersonId, m => new PersonInfo(m.PersonId, m.DisplayName, m.UserId));
    }

    public async Task<DebtWallet> WalletAsync(Guid userId, Guid walletId, CancellationToken cancellationToken)
    {
        var wallet = await dbContext.Wallets.AsNoTracking()
            .SingleOrDefaultAsync(w => w.WalletId == walletId, cancellationToken);

        // A wallet created a moment ago may not have reached this module yet: let the client retry.
        if (wallet is null)
            throw new NotYetAvailableException($"Wallet '{walletId}' is not available yet. Try again in a moment.");

        if (wallet.UserId != userId)
            throw new NotFoundException("Wallet", walletId);

        return wallet;
    }

    private async Task<Guid?> RequireWalletIfMoneyMovesAsync(
        Guid userId,
        Guid? walletId,
        decimal amount,
        Currency currency,
        bool required,
        CancellationToken cancellationToken)
    {
        if (amount == 0 || (walletId is null && !required))
            return null;

        if (walletId is null)
            throw new DomainException("settlement.wallet_required", "Choose the wallet the money comes from or goes to.");

        var wallet = await WalletAsync(userId, walletId.Value, cancellationToken);

        if (wallet.Currency != currency)
            throw new DomainException(
                "settlement.currency_mismatch",
                $"The wallet holds {wallet.Currency} but the settlement is in {currency}.");

        return wallet.WalletId;
    }

    private static Dictionary<Guid, PersonInfo> People(params PersonInfo[] persons)
        => persons.ToDictionary(p => p.PersonId);
}
