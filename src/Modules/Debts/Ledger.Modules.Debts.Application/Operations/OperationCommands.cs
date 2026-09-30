using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Contracts;
using Ledger.Modules.Debts.Domain;
using Ledger.Modules.Transactions.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application.Operations;

public sealed record OperationDto(
    Guid Id,
    OperationKind Kind,
    OperationStatus Status,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record RecordLoanCommand(
    Guid UserId,
    Guid ContactId,
    Guid WalletId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Description) : ICommand<OperationDto>;

/// <param name="WalletId">Only for my own payer entry: the wallet I paid from.</param>
public sealed record SplitPayer(Guid PersonId, decimal Amount, Guid? WalletId = null);

/// <param name="Value">Percentage (Percent) or amount (Custom); ignored for Equal.</param>
public sealed record SplitParticipant(Guid PersonId, decimal? Value = null);

public sealed record RecordSplitCommand(
    Guid UserId,
    Guid? GroupId,
    decimal Total,
    string Currency,
    SplitMethod Method,
    IReadOnlyList<SplitPayer> Payers,
    IReadOnlyList<SplitParticipant> Participants,
    Guid? CategoryId,
    DateTimeOffset OccurredAt,
    string? Description) : ICommand<OperationDto>;

/// <summary>
/// Full settlement. Scope Debt needs DebtId; Contact needs ContactId and Currency;
/// GroupPair needs GroupId, FromPersonId, ToPersonId and Currency.
/// WalletId is required whenever money leaves or enters my pocket.
/// </summary>
public sealed record SettleCommand(
    Guid UserId,
    SettlementScope Scope,
    Guid? DebtId,
    Guid? ContactId,
    Guid? GroupId,
    Guid? FromPersonId,
    Guid? ToPersonId,
    string? Currency,
    Guid? WalletId,
    DateTimeOffset OccurredAt,
    string? Description) : ICommand<OperationDto>;

/// <summary>Internal: the ledger answered a posting request.</summary>
public sealed record ApplyLedgerResultCommand(Guid OperationId, bool Succeeded, string? Reason) : ICommand;

internal sealed class RecordLoanValidator : AbstractValidator<RecordLoanCommand>
{
    public RecordLoanValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(19, Money.Decimals, true);
        RuleFor(x => x.Description).MaximumLength(DebtOperation.DescriptionMaxLength);
    }
}

internal sealed class RecordSplitValidator : AbstractValidator<RecordSplitCommand>
{
    public RecordSplitValidator()
    {
        RuleFor(x => x.Total).GreaterThan(0).PrecisionScale(19, Money.Decimals, true);
        RuleFor(x => x.Currency).Must(Currencies.IsSupported).WithMessage("Unsupported currency.");
        RuleFor(x => x.Payers).NotEmpty();
        RuleFor(x => x.Participants).NotEmpty();
        RuleFor(x => x.Description).MaximumLength(DebtOperation.DescriptionMaxLength);
    }
}

internal sealed class SettleValidator : AbstractValidator<SettleCommand>
{
    public SettleValidator()
    {
        RuleFor(x => x.DebtId).NotNull().When(x => x.Scope == SettlementScope.Debt);
        RuleFor(x => x.ContactId).NotNull().When(x => x.Scope == SettlementScope.Contact);
        RuleFor(x => x.GroupId).NotNull().When(x => x.Scope == SettlementScope.GroupPair);
        RuleFor(x => x.FromPersonId).NotNull().When(x => x.Scope == SettlementScope.GroupPair);
        RuleFor(x => x.ToPersonId).NotNull().When(x => x.Scope == SettlementScope.GroupPair);
        RuleFor(x => x.Currency).Must(Currencies.IsSupported).WithMessage("Unsupported currency.")
            .When(x => x.Scope != SettlementScope.Debt);
        RuleFor(x => x.Description).MaximumLength(DebtOperation.DescriptionMaxLength);
    }
}

internal sealed class OperationHandlers(
    IDebtsDbContext dbContext,
    IDebtsEventBus eventBus,
    PeopleDirectory people,
    GroupBalances groupBalances)
    : ICommandHandler<RecordLoanCommand, OperationDto>,
      ICommandHandler<RecordSplitCommand, OperationDto>,
      ICommandHandler<SettleCommand, OperationDto>,
      ICommandHandler<ApplyLedgerResultCommand>
{
    public async Task<OperationDto> Handle(RecordLoanCommand command, CancellationToken cancellationToken)
    {
        var me = await people.MeAsync(command.UserId, cancellationToken);
        var borrower = await people.ContactAsync(command.UserId, command.ContactId, cancellationToken);
        var wallet = await GetWalletAsync(command.UserId, command.WalletId, cancellationToken);

        var loan = DebtOperation.Loan(
            command.UserId, borrower.PersonId, wallet.WalletId, command.Amount, wallet.Currency,
            command.OccurredAt, command.Description);

        var debt = Debt.Create(loan.Id, null, me.PersonId, borrower.PersonId, loan.Total, loan.Currency);

        dbContext.Operations.Add(loan);
        dbContext.Debts.Add(debt);

        await RequestPostingAsync(loan, LedgerLegs.ForLoan(loan, me, borrower), cancellationToken);

        return ToDto(loan);
    }

    public async Task<OperationDto> Handle(RecordSplitCommand command, CancellationToken cancellationToken)
    {
        var currency = Currencies.Parse(command.Currency);

        IReadOnlyDictionary<Guid, PersonInfo> allowed = command.GroupId is { } groupId
            ? (await people.GroupAsync(command.UserId, groupId, cancellationToken)).Members
            : await people.CircleAsync(command.UserId, cancellationToken);

        var involved = command.Payers.Select(p => p.PersonId).Concat(command.Participants.Select(p => p.PersonId));

        foreach (var personId in involved.Distinct())
        {
            if (!allowed.ContainsKey(personId))
                throw new DomainException(
                    "split.unknown_person",
                    command.GroupId is null
                        ? $"Person '{personId}' is neither you nor one of your contacts."
                        : $"Person '{personId}' is not a member of the group.");
        }

        var payers = new List<OperationPayer>();

        foreach (var payer in command.Payers)
        {
            Guid? walletId = null;

            if (payer.PersonId == command.UserId)
            {
                if (payer.WalletId is null)
                    throw new DomainException("split.wallet_required", "Choose the wallet you paid from.");

                var wallet = await GetWalletAsync(command.UserId, payer.WalletId.Value, cancellationToken);

                if (wallet.Currency != currency)
                    throw new DomainException(
                        "split.currency_mismatch",
                        $"The wallet holds {wallet.Currency} but the expense is in {currency}.");

                walletId = wallet.WalletId;
            }

            payers.Add(new OperationPayer(payer.PersonId, payer.Amount, walletId));
        }

        var shares = SplitMath.ComputeShares(
            command.Total,
            command.Method,
            command.Participants.Select(p => new Participant(p.PersonId, p.Value)).ToList());

        var split = DebtOperation.Split(
            command.UserId, command.GroupId, command.Total, currency, command.Method, command.CategoryId,
            payers, shares, command.OccurredAt, command.Description);

        var debts = SplitMath
            .AllocateDebts(payers.Select(p => (p.PersonId, p.Amount)).ToList(), shares)
            .Select(d => Debt.Create(split.Id, split.GroupId, d.CreditorPersonId, d.DebtorPersonId, d.Amount, currency))
            .ToList();

        if (debts.Count == 0)
            throw new DomainException(
                "split.no_debts",
                "Nobody owes anything in this split. Record it as a regular expense instead.");

        dbContext.Operations.Add(split);
        dbContext.Debts.AddRange(debts);

        var legs = split.GroupId is null
            ? LedgerLegs.ForSplit(split, debts, allowed)
            : LedgerLegs.ForGroupSplit(split, allowed);

        await RequestPostingAsync(split, legs, cancellationToken);

        return ToDto(split);
    }

    public async Task<OperationDto> Handle(SettleCommand command, CancellationToken cancellationToken)
        => command.Scope switch
        {
            SettlementScope.Debt => await SettleDebtAsync(command, cancellationToken),
            SettlementScope.Contact => await SettleContactAsync(command, cancellationToken),
            SettlementScope.GroupPair => await SettleGroupPairAsync(command, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };

    public async Task Handle(ApplyLedgerResultCommand command, CancellationToken cancellationToken)
    {
        var operation = await dbContext.Operations
            .SingleOrDefaultAsync(o => o.Id == command.OperationId, cancellationToken)
            ?? throw new NotYetAvailableException($"Operation '{command.OperationId}' is not stored yet.");

        if (operation.Status != OperationStatus.Pending)
            return;

        if (command.Succeeded)
            await CompleteAsync(operation, cancellationToken);
        else
            await RejectAsync(operation, command.Reason ?? "Rejected by the ledger.", cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<OperationDto> SettleDebtAsync(SettleCommand command, CancellationToken cancellationToken)
    {
        var debt = await dbContext.Debts.SingleOrDefaultAsync(d => d.Id == command.DebtId, cancellationToken);

        if (debt is null || !debt.Involves(command.UserId))
            throw new NotFoundException("Debt", command.DebtId!);

        if (debt.GroupId is not null)
            throw new DomainException(
                "settlement.group_debt",
                "Group debts are settled with the group's suggested payments.");

        var circle = await people.CircleAsync(command.UserId, cancellationToken);
        var other = circle.GetValueOrDefault(debt.CreditorPersonId == command.UserId ? debt.DebtorPersonId : debt.CreditorPersonId)
            ?? throw new NotYetAvailableException("The other person is not in your contacts yet. Try again in a moment.");

        return await SettleDebtsAsync(command, circle[command.UserId], other, [debt], debt.Currency, cancellationToken);
    }

    private async Task<OperationDto> SettleContactAsync(SettleCommand command, CancellationToken cancellationToken)
    {
        var me = await people.MeAsync(command.UserId, cancellationToken);
        var contact = await people.ContactAsync(command.UserId, command.ContactId!.Value, cancellationToken);
        var currency = Currencies.Parse(command.Currency!);

        var debts = await dbContext.Debts
            .Where(d => d.GroupId == null
                        && d.Status == DebtStatus.Open
                        && d.Currency == currency
                        && ((d.CreditorPersonId == me.PersonId && d.DebtorPersonId == contact.PersonId)
                            || (d.CreditorPersonId == contact.PersonId && d.DebtorPersonId == me.PersonId)))
            .ToListAsync(cancellationToken);

        if (debts.Count == 0)
            throw new DomainException("settlement.nothing", $"There are no open {currency} debts with {contact.Name}.");

        return await SettleDebtsAsync(command, me, contact, debts, currency, cancellationToken);
    }

    private async Task<OperationDto> SettleDebtsAsync(
        SettleCommand command,
        PersonInfo me,
        PersonInfo other,
        IReadOnlyList<Debt> debts,
        Currency currency,
        CancellationToken cancellationToken)
    {
        var owedToMe = debts.Where(d => d.CreditorPersonId == me.PersonId).Sum(d => d.Amount);
        var iOwe = debts.Where(d => d.DebtorPersonId == me.PersonId).Sum(d => d.Amount);
        var net = owedToMe - iOwe;

        var walletId = await RequireWalletIfMoneyMovesAsync(command, net, currency, cancellationToken);

        var settlement = DebtOperation.Settlement(
            command.UserId,
            command.Scope,
            null,
            net >= 0 ? other.PersonId : me.PersonId,
            net >= 0 ? me.PersonId : other.PersonId,
            Math.Abs(net),
            currency,
            walletId,
            command.OccurredAt,
            command.Description ?? $"Settle up with {other.Name}");

        foreach (var debt in debts)
            debt.StartSettlement(settlement.Id);

        dbContext.Operations.Add(settlement);

        var legs = LedgerLegs.ForDebtSettlement(settlement, debts, me, other, command.UserId, walletId);

        await RequestPostingAsync(settlement, legs, cancellationToken);

        return ToDto(settlement);
    }

    private async Task<OperationDto> SettleGroupPairAsync(SettleCommand command, CancellationToken cancellationToken)
    {
        var groupId = command.GroupId!.Value;
        var currency = Currencies.Parse(command.Currency!);
        var (groupName, members) = await people.GroupAsync(command.UserId, groupId, cancellationToken);

        var from = members.GetValueOrDefault(command.FromPersonId!.Value)
            ?? throw new DomainException("settlement.not_member", "The payer is not a member of the group.");
        var to = members.GetValueOrDefault(command.ToPersonId!.Value)
            ?? throw new DomainException("settlement.not_member", "The receiver is not a member of the group.");

        var pending = await dbContext.Operations.AnyAsync(
            o => o.GroupId == groupId
                 && o.Status == OperationStatus.Pending
                 && o.Currency == currency,
            cancellationToken);

        if (pending)
            throw new ConflictException("Another operation of this group is being recorded. Try again in a moment.");

        var balances = (await groupBalances.ComputeAsync(groupId, members.Keys, cancellationToken))
            .GetValueOrDefault(currency) ?? [];

        var suggestion = SplitMath.Simplify(balances)
            .SingleOrDefault(p => p.FromPersonId == from.PersonId && p.ToPersonId == to.PersonId)
            ?? throw new DomainException(
                "settlement.not_suggested",
                $"{from.Name} does not have to pay {to.Name} in {currency}. Check the group balances.");

        var iMoveMoney = from.UserId == command.UserId || to.UserId == command.UserId;
        var walletId = iMoveMoney
            ? await RequireWalletIfMoneyMovesAsync(command, suggestion.Amount, currency, cancellationToken)
            : null;

        var settlement = DebtOperation.Settlement(
            command.UserId,
            SettlementScope.GroupPair,
            groupId,
            from.PersonId,
            to.PersonId,
            suggestion.Amount,
            currency,
            walletId,
            command.OccurredAt,
            command.Description ?? $"{groupName}: {from.Name} paid {to.Name}");

        dbContext.Operations.Add(settlement);

        await RequestPostingAsync(
            settlement,
            LedgerLegs.ForGroupSettlement(settlement, from, to, command.UserId, walletId),
            cancellationToken);

        return ToDto(settlement);
    }

    private async Task<Guid?> RequireWalletIfMoneyMovesAsync(
        SettleCommand command,
        decimal amount,
        Currency currency,
        CancellationToken cancellationToken)
    {
        if (amount == 0)
            return null;

        if (command.WalletId is null)
            throw new DomainException("settlement.wallet_required", "Choose the wallet the money comes from or goes to.");

        var wallet = await GetWalletAsync(command.UserId, command.WalletId.Value, cancellationToken);

        if (wallet.Currency != currency)
            throw new DomainException(
                "settlement.currency_mismatch",
                $"The wallet holds {wallet.Currency} but the settlement is in {currency}.");

        return wallet.WalletId;
    }

    /// <summary>
    /// Saves the operation and asks the ledger to record it. When no registered user is affected
    /// (e.g. a group expense between two non-registered people) it completes right away.
    /// </summary>
    private async Task RequestPostingAsync(
        DebtOperation operation,
        IReadOnlyList<LedgerLeg> legs,
        CancellationToken cancellationToken)
    {
        if (legs.Count == 0)
        {
            await CompleteAsync(operation, cancellationToken);
        }
        else
        {
            await eventBus.PublishAsync(
                new LedgerPostingRequested(
                    operation.Id,
                    operation.Kind.ToString(),
                    operation.OccurredAt,
                    operation.Description,
                    operation.Currency.ToString(),
                    operation.InitiatorUserId,
                    legs),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteAsync(DebtOperation operation, CancellationToken cancellationToken)
    {
        operation.Complete();

        var created = await dbContext.Debts.Where(d => d.OperationId == operation.Id).ToListAsync(cancellationToken);
        var local = dbContext.Debts.Local.Where(d => d.OperationId == operation.Id);

        foreach (var debt in created.Union(local))
            debt.Activate();

        if (operation.Kind == OperationKind.Settlement)
        {
            var settled = await dbContext.Debts
                .Where(d => d.SettlementOperationId == operation.Id)
                .ToListAsync(cancellationToken);

            foreach (var debt in settled.Union(dbContext.Debts.Local.Where(d => d.SettlementOperationId == operation.Id)))
                debt.CompleteSettlement(operation.Id);

            if (operation.GroupId is { } groupId)
                await SettleGroupDebtsIfBalancedAsync(operation, groupId, cancellationToken);
        }

        await eventBus.PublishAsync(
            new DebtOperationCompleted(
                operation.Id,
                operation.Kind.ToString(),
                operation.InitiatorUserId,
                operation.GroupId,
                await PartiesAsync(operation, cancellationToken)),
            cancellationToken);
    }

    private async Task RejectAsync(DebtOperation operation, string reason, CancellationToken cancellationToken)
    {
        operation.Reject(reason);

        foreach (var debt in await dbContext.Debts.Where(d => d.OperationId == operation.Id).ToListAsync(cancellationToken))
            debt.Reject();

        foreach (var debt in await dbContext.Debts.Where(d => d.SettlementOperationId == operation.Id).ToListAsync(cancellationToken))
            debt.CancelSettlement(operation.Id);
    }

    /// <summary>Once every member of a group is even in a currency, all its debts in that currency are settled.</summary>
    private async Task SettleGroupDebtsIfBalancedAsync(
        DebtOperation settlement,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var memberIds = await dbContext.GroupMembers
            .Where(m => m.GroupId == groupId)
            .Select(m => m.PersonId)
            .ToListAsync(cancellationToken);

        var balances = await groupBalances.ComputeAsync(groupId, memberIds, cancellationToken);

        // The settlement being completed is not persisted yet: apply it on top.
        var currencyBalances = balances.GetValueOrDefault(settlement.Currency) ?? [];
        currencyBalances[settlement.FromPersonId!.Value] = currencyBalances.GetValueOrDefault(settlement.FromPersonId.Value) + settlement.Total;
        currencyBalances[settlement.ToPersonId!.Value] = currencyBalances.GetValueOrDefault(settlement.ToPersonId.Value) - settlement.Total;

        if (currencyBalances.Values.Any(v => v != 0))
            return;

        var open = await dbContext.Debts
            .Where(d => d.GroupId == groupId && d.Currency == settlement.Currency && d.Status == DebtStatus.Open)
            .ToListAsync(cancellationToken);

        foreach (var debt in open)
            debt.CompleteSettlement(settlement.Id);
    }

    private async Task<IReadOnlyList<DebtParty>> PartiesAsync(DebtOperation operation, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, PersonInfo> known = operation.GroupId is { } groupId
            ? (await people.GroupAsync(operation.InitiatorUserId, groupId, cancellationToken)).Members
            : await people.CircleAsync(operation.InitiatorUserId, cancellationToken);

        var personIds = LedgerLegs.Involved(operation)
            .Append(operation.InitiatorUserId)
            .Concat(new[] { operation.FromPersonId, operation.ToPersonId }.OfType<Guid>())
            .Distinct();

        return personIds
            .Where(known.ContainsKey)
            .Select(id => known[id])
            .Select(p => new DebtParty(p.PersonId, p.Name, p.UserId))
            .ToList();
    }

    private async Task<DebtWallet> GetWalletAsync(Guid userId, Guid walletId, CancellationToken cancellationToken)
    {
        var wallet = await dbContext.Wallets.AsNoTracking()
            .SingleOrDefaultAsync(w => w.WalletId == walletId, cancellationToken);

        if (wallet is null || wallet.UserId != userId)
            throw new NotFoundException("Wallet", walletId);

        return wallet;
    }

    internal static OperationDto ToDto(DebtOperation operation)
        => new(operation.Id, operation.Kind, operation.Status, operation.RejectionReason, operation.CreatedAt, operation.CompletedAt);
}
