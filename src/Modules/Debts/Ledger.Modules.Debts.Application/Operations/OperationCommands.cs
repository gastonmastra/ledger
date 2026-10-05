using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Contracts;
using Ledger.Modules.Debts.Application.Links;
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

/// <summary>
/// Records a debt that existed before using Ledger (outside groups). It is neither income nor
/// expense: the balance comes from Opening balances.
/// </summary>
public sealed record RecordInitialDebtCommand(
    Guid UserId,
    Guid ContactId,
    InitialDebtDirection Direction,
    decimal Amount,
    string Currency,
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
        RuleFor(x => x.OccurredAt).NotInFuture();
    }
}

internal sealed class RecordInitialDebtValidator : AbstractValidator<RecordInitialDebtCommand>
{
    public RecordInitialDebtValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(19, Money.Decimals, true);
        RuleFor(x => x.Currency).Must(Currencies.IsSupported).WithMessage("Unsupported currency.");
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(DebtOperation.DescriptionMaxLength);
        RuleFor(x => x.OccurredAt).NotInFuture();
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
        RuleFor(x => x.OccurredAt).NotInFuture();
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
        RuleFor(x => x.OccurredAt).NotInFuture();
    }
}

internal sealed class OperationHandlers(
    IDebtsDbContext dbContext,
    PeopleDirectory people,
    OperationFactory factory,
    OperationLifecycle lifecycle,
    BooksLockGuard locks,
    GroupBalances groupBalances,
    MergeGuard merges)
    : ICommandHandler<RecordLoanCommand, OperationDto>,
      ICommandHandler<RecordSplitCommand, OperationDto>,
      ICommandHandler<RecordInitialDebtCommand, OperationDto>,
      ICommandHandler<SettleCommand, OperationDto>,
      ICommandHandler<ApplyLedgerResultCommand>
{
    public async Task<OperationDto> Handle(RecordLoanCommand command, CancellationToken cancellationToken)
    {
        var borrower = await people.ContactAsync(command.UserId, command.ContactId, cancellationToken);

        var built = await factory.LoanAsync(
            command.UserId, borrower, command.WalletId, command.Amount, command.OccurredAt, command.Description,
            cancellationToken);

        return await RecordAsync(built, command.UserId, cancellationToken);
    }

    public async Task<OperationDto> Handle(RecordInitialDebtCommand command, CancellationToken cancellationToken)
    {
        var contact = await people.ContactAsync(command.UserId, command.ContactId, cancellationToken);

        var built = await factory.InitialDebtAsync(
            command.UserId, contact, command.Direction, command.Amount, Currencies.Parse(command.Currency),
            command.OccurredAt, command.Description, cancellationToken);

        return await RecordAsync(built, command.UserId, cancellationToken);
    }

    public async Task<OperationDto> Handle(RecordSplitCommand command, CancellationToken cancellationToken)
    {
        if (command.GroupId is { } groupId)
            await people.GroupAsync(command.UserId, groupId, cancellationToken);

        var built = await factory.SplitAsync(
            command.UserId, command.GroupId, command.Total, Currencies.Parse(command.Currency), command.Method,
            command.Payers, command.Participants, command.CategoryId, command.OccurredAt, command.Description,
            requireMyWallet: true, cancellationToken);

        return await RecordAsync(built, command.UserId, cancellationToken);
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
            await lifecycle.CompleteAsync(operation, cancellationToken);
        else
            await lifecycle.RejectAsync(operation, command.Reason ?? "Rejected by the ledger.", cancellationToken);

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

        return await SettleDebtsAsync(command, other, [debt], debt.Currency, cancellationToken);
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

        return await SettleDebtsAsync(command, contact, debts, currency, cancellationToken);
    }

    private async Task<OperationDto> SettleDebtsAsync(
        SettleCommand command,
        PersonInfo other,
        IReadOnlyList<Debt> debts,
        Currency currency,
        CancellationToken cancellationToken)
    {
        var operationIds = debts.Select(d => d.OperationId).Distinct().ToList();

        if (await dbContext.Operations.AnyAsync(o => operationIds.Contains(o.Id) && o.PendingChangeId != null, cancellationToken))
            throw new DomainException(
                "operation.change_pending",
                "A change of one of these debts is waiting for confirmation. Settle it once it is answered.");

        var built = await factory.DebtSettlementAsync(
            command.UserId, command.Scope, other, debts, currency, command.WalletId, command.OccurredAt,
            command.Description, cancellationToken);

        foreach (var debt in debts)
            debt.StartSettlement(built.Operation.Id);

        return await RecordAsync(built, command.UserId, cancellationToken);
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

        if (await dbContext.Operations.AnyAsync(o => o.GroupId == groupId && o.PendingChangeId != null, cancellationToken))
            throw new DomainException(
                "operation.change_pending",
                "A change of a group expense is waiting for confirmation. Settle up once it is answered.");

        var balances = (await groupBalances.ComputeAsync(groupId, members.Keys, cancellationToken))
            .GetValueOrDefault(currency) ?? [];

        var suggestion = SplitMath.Simplify(balances)
            .SingleOrDefault(p => p.FromPersonId == from.PersonId && p.ToPersonId == to.PersonId)
            ?? throw new DomainException(
                "settlement.not_suggested",
                $"{from.Name} does not have to pay {to.Name} in {currency}. Check the group balances.");

        var built = await factory.GroupSettlementAsync(
            command.UserId, groupId, from, to, suggestion.Amount, currency, command.WalletId, command.OccurredAt,
            command.Description ?? $"{groupName}: {from.Name} paid {to.Name}", cancellationToken);

        return await RecordAsync(built, command.UserId, cancellationToken);
    }

    private async Task<OperationDto> RecordAsync(BuiltOperation built, Guid userId, CancellationToken cancellationToken)
    {
        await locks.EnsureOpenAsync(built.RegisteredUserIds, [built.Operation.OccurredAt], userId, cancellationToken);
        await merges.EnsureNotMergingAsync(built.Operation.InvolvedPersonIds, cancellationToken);

        dbContext.Operations.Add(built.Operation);
        dbContext.Debts.AddRange(built.Debts);

        await lifecycle.RequestPostingAsync(built.Operation, built.Legs, cancellationToken);

        return ToDto(built.Operation);
    }

    internal static OperationDto ToDto(DebtOperation operation)
        => new(operation.Id, operation.Kind, operation.Status, operation.RejectionReason, operation.CreatedAt, operation.CompletedAt);
}

/// <summary>How an operation moves from Pending to Completed or Rejected, and what that does to its debts.</summary>
internal sealed class OperationLifecycle(
    IDebtsDbContext dbContext,
    IDebtsEventBus eventBus,
    OperationFactory factory,
    GroupBalances groupBalances)
{
    /// <summary>
    /// Saves the operation and asks the ledger to record it. When no registered user is affected
    /// (e.g. a group expense between two non-registered people) it completes right away.
    /// </summary>
    public async Task RequestPostingAsync(
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

    public async Task CompleteAsync(DebtOperation operation, CancellationToken cancellationToken)
    {
        operation.Complete();

        foreach (var debt in await DebtsOfAsync(operation.Id, cancellationToken))
            debt.Activate();

        if (operation.Kind == OperationKind.Settlement)
        {
            foreach (var debt in await SettledByAsync(operation.Id, cancellationToken))
                debt.CompleteSettlement(operation.Id);

            if (operation.GroupId is { } groupId)
                await SettleGroupDebtsIfBalancedAsync(operation, groupId, cancellationToken);
        }

        await PublishCompletedAsync(operation, cancellationToken);
    }

    public async Task RejectAsync(DebtOperation operation, string reason, CancellationToken cancellationToken)
    {
        operation.Reject(reason);

        foreach (var debt in await DebtsOfAsync(operation.Id, cancellationToken))
            debt.Reject();

        foreach (var debt in await SettledByAsync(operation.Id, cancellationToken))
            debt.CancelSettlement(operation.Id);
    }

    public async Task PublishCompletedAsync(DebtOperation operation, CancellationToken cancellationToken)
        => await eventBus.PublishAsync(
            new DebtOperationCompleted(
                operation.Id,
                operation.Kind.ToString(),
                operation.InitiatorUserId,
                operation.GroupId,
                await PartiesAsync(operation, cancellationToken)),
            cancellationToken);

    /// <summary>The debts created by the operation (stored or just added).</summary>
    public async Task<IReadOnlyList<Debt>> DebtsOfAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var stored = await dbContext.Debts.Where(d => d.OperationId == operationId).ToListAsync(cancellationToken);
        return stored.Union(dbContext.Debts.Local.Where(d => d.OperationId == operationId)).ToList();
    }

    /// <summary>The debts a settlement pays (stored or just added).</summary>
    public async Task<IReadOnlyList<Debt>> SettledByAsync(Guid settlementId, CancellationToken cancellationToken)
    {
        var stored = await dbContext.Debts.Where(d => d.SettlementOperationId == settlementId).ToListAsync(cancellationToken);
        return stored.Union(dbContext.Debts.Local.Where(d => d.SettlementOperationId == settlementId)).ToList();
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
        var known = await factory.PeopleOfAsync(operation, cancellationToken);

        return operation.InvolvedPersonIds
            .Where(known.ContainsKey)
            .Select(id => known[id])
            .Select(p => new DebtParty(p.PersonId, p.Name, p.UserId))
            .ToList();
    }
}
