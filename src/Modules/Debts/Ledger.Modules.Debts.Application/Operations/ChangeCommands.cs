using System.Text.Json;
using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Application.Links;
using Ledger.Modules.Debts.Domain;
using Ledger.Modules.Transactions.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application.Operations;

/// <summary>
/// New values for an operation. Every field of the kind is sent again (the form is prefilled).
/// Loan: ContactId, WalletId, Amount. Existing debt: ContactId, Direction, Amount, Currency.
/// Split: Amount (total), Currency, Method, Payers, Participants, CategoryId. Settlement: WalletId.
/// The contact, wallets, category and currency belong to whoever recorded the operation: for
/// anybody else they are kept as they were.
/// </summary>
public sealed record ReviseOperationCommand(
    Guid UserId,
    Guid OperationId,
    string Reason,
    DateTimeOffset OccurredAt,
    string? Description,
    decimal? Amount = null,
    string? Currency = null,
    Guid? ContactId = null,
    InitialDebtDirection? Direction = null,
    Guid? WalletId = null,
    SplitMethod? Method = null,
    IReadOnlyList<SplitPayer>? Payers = null,
    IReadOnlyList<SplitParticipant>? Participants = null,
    Guid? CategoryId = null) : ICommand<ChangeResultDto>;

public sealed record DeleteOperationCommand(Guid UserId, Guid OperationId, string Reason) : ICommand<ChangeResultDto>;

public sealed record RestoreOperationCommand(Guid UserId, Guid OperationId, string Reason) : ICommand<ChangeResultDto>;

public sealed record RespondToChangeCommand(Guid UserId, Guid ChangeId, bool Accept, string? Reason) : ICommand<ChangeResultDto>;

/// <summary>Internal: the ledger answered a revision request.</summary>
public sealed record ApplyRevisionResultCommand(Guid ChangeId, bool Succeeded, string? Reason) : ICommand;

/// <param name="WaitingFor">Names of the people who still have to accept or reject it.</param>
public sealed record ChangeResultDto(
    Guid ChangeId,
    Guid OperationId,
    ChangeKind Kind,
    ChangeStatus Status,
    Guid? NewOperationId,
    IReadOnlyList<string> WaitingFor);

/// <summary>The new version as stored in a change (JSON), from the initiator's point of view.</summary>
internal sealed record OperationDraft(
    DateTimeOffset OccurredAt,
    string? Description,
    decimal Amount,
    string Currency,
    Guid? CounterpartyPersonId = null,
    InitialDebtDirection? Direction = null,
    Guid? WalletId = null,
    SplitMethod? Method = null,
    IReadOnlyList<SplitPayer>? Payers = null,
    IReadOnlyList<SplitParticipant>? Participants = null,
    Guid? CategoryId = null,
    SplitMethod? RecordAs = null);

/// <summary>What the operation looks like (stored with a change to show what it proposes).</summary>
public sealed record OperationSnapshot(
    decimal Total,
    string Currency,
    DateTimeOffset OccurredAt,
    string? Description,
    Guid? FromPersonId,
    Guid? ToPersonId,
    IReadOnlyList<SnapshotLine> Payers,
    IReadOnlyList<SnapshotLine> Shares)
{
    public static OperationSnapshot Of(DebtOperation operation)
        => new(
            operation.Total,
            operation.Currency.ToString(),
            operation.OccurredAt,
            operation.Description,
            operation.FromPersonId,
            operation.ToPersonId,
            operation.Payers.Select(p => new SnapshotLine(p.PersonId, p.Amount)).ToList(),
            operation.Shares.Select(s => new SnapshotLine(s.PersonId, s.Amount)).ToList());
}

public sealed record SnapshotLine(Guid PersonId, decimal Amount);

internal sealed class ReviseOperationValidator : AbstractValidator<ReviseOperationCommand>
{
    public ReviseOperationValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(OperationChange.ReasonMaxLength);
        RuleFor(x => x.Description).MaximumLength(DebtOperation.DescriptionMaxLength);
        RuleFor(x => x.OccurredAt).NotInFuture();
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(19, Money.Decimals, true).When(x => x.Amount is not null);
        RuleFor(x => x.Currency).Must(Currencies.IsSupported).WithMessage("Unsupported currency.").When(x => x.Currency is not null);
    }
}

internal sealed class DeleteOperationValidator : AbstractValidator<DeleteOperationCommand>
{
    public DeleteOperationValidator()
        => RuleFor(x => x.Reason).NotEmpty().MaximumLength(OperationChange.ReasonMaxLength);
}

internal sealed class RestoreOperationValidator : AbstractValidator<RestoreOperationCommand>
{
    public RestoreOperationValidator()
        => RuleFor(x => x.Reason).NotEmpty().MaximumLength(OperationChange.ReasonMaxLength);
}

internal sealed class RespondToChangeValidator : AbstractValidator<RespondToChangeCommand>
{
    public RespondToChangeValidator()
        => RuleFor(x => x.Reason).MaximumLength(OperationChange.ReasonMaxLength);
}

/// <summary>
/// Changes to shared operations: nothing is edited in place. A change builds a new version; the
/// linked users involved confirm it; then the ledger reverses the previous version and records the
/// new one in the books of the author and of everybody who accepted.
/// </summary>
internal sealed class ChangeHandlers(
    IDebtsDbContext dbContext,
    IDebtsEventBus eventBus,
    PeopleDirectory people,
    OperationFactory factory,
    OperationLifecycle lifecycle,
    BooksLockGuard locks,
    MergeGuard merges)
    : ICommandHandler<ReviseOperationCommand, ChangeResultDto>,
      ICommandHandler<DeleteOperationCommand, ChangeResultDto>,
      ICommandHandler<RestoreOperationCommand, ChangeResultDto>,
      ICommandHandler<RespondToChangeCommand, ChangeResultDto>,
      ICommandHandler<ApplyRevisionResultCommand>
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ChangeResultDto> Handle(ReviseOperationCommand command, CancellationToken cancellationToken)
    {
        var operation = await LoadAsync(command.OperationId, cancellationToken);
        var peopleOfOperation = await factory.PeopleOfAsync(operation, cancellationToken);

        await EnsureMayChangeAsync(operation, command.UserId, peopleOfOperation, cancellationToken);
        await EnsureNotPaidAsync(operation, cancellationToken);

        var draft = await DraftFromAsync(operation, command, cancellationToken);

        // Built now to validate it (and show what it proposes); built again when it is applied.
        var built = await BuildAsync(operation, draft, requireMyWallet: command.UserId == operation.InitiatorUserId, cancellationToken);

        return await RequestAsync(operation, ChangeKind.Revise, command.UserId, command.Reason, draft, built, peopleOfOperation, cancellationToken);
    }

    public async Task<ChangeResultDto> Handle(DeleteOperationCommand command, CancellationToken cancellationToken)
    {
        var operation = await LoadAsync(command.OperationId, cancellationToken);
        var peopleOfOperation = await factory.PeopleOfAsync(operation, cancellationToken);

        await EnsureMayChangeAsync(operation, command.UserId, peopleOfOperation, cancellationToken);
        await EnsureNotPaidAsync(operation, cancellationToken);

        return await RequestAsync(operation, ChangeKind.Delete, command.UserId, command.Reason, null, null, peopleOfOperation, cancellationToken);
    }

    public async Task<ChangeResultDto> Handle(RestoreOperationCommand command, CancellationToken cancellationToken)
    {
        var operation = await LoadAsync(command.OperationId, cancellationToken);
        var peopleOfOperation = await factory.PeopleOfAsync(operation, cancellationToken);

        await EnsureMayChangeAsync(operation, command.UserId, peopleOfOperation, cancellationToken);

        if (operation.Status != OperationStatus.Deleted)
            throw new DomainException("operation.not_deleted", "Only a deleted operation can be restored.");

        if (operation.Kind == OperationKind.Settlement)
            throw new DomainException(
                "operation.not_restorable",
                "A deleted settlement cannot be restored: settle the debts again instead.");

        var newer = await dbContext.Operations.AnyAsync(
            o => o.EntryId == operation.EntryId && o.Revision > operation.Revision && o.Status != OperationStatus.Rejected,
            cancellationToken);

        if (newer)
            throw new DomainException("operation.not_latest", "Only the latest version of an operation can be restored.");

        var draft = DraftOf(operation);
        var built = await BuildAsync(operation, draft, requireMyWallet: false, cancellationToken);

        return await RequestAsync(operation, ChangeKind.Restore, command.UserId, command.Reason, draft, built, peopleOfOperation, cancellationToken);
    }

    public async Task<ChangeResultDto> Handle(RespondToChangeCommand command, CancellationToken cancellationToken)
    {
        var change = await dbContext.Changes
            .Include(c => c.Approvals)
            .SingleOrDefaultAsync(c => c.Id == command.ChangeId, cancellationToken);

        if (change is null || change.Approvals.All(a => a.UserId != command.UserId))
            throw new NotFoundException("Change", command.ChangeId);

        change.Respond(command.UserId, command.Accept, command.Reason);

        if (change.AllAnswered)
            await ApplyAsync(change, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return await ResultAsync(change, cancellationToken);
    }

    public async Task Handle(ApplyRevisionResultCommand command, CancellationToken cancellationToken)
    {
        var change = await dbContext.Changes
            .Include(c => c.Approvals)
            .SingleOrDefaultAsync(c => c.Id == command.ChangeId, cancellationToken)
            ?? throw new NotYetAvailableException($"Change '{command.ChangeId}' is not stored yet.");

        if (change.Status != ChangeStatus.Applying)
            return;

        if (command.Succeeded)
            await FinishAsync(change, cancellationToken);
        else
            await FailAsync(change, command.Reason ?? "Rejected by the ledger.", cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ChangeResultDto> RequestAsync(
        DebtOperation operation,
        ChangeKind kind,
        Guid authorUserId,
        string reason,
        OperationDraft? draft,
        BuiltOperation? built,
        IReadOnlyDictionary<Guid, PersonInfo> peopleOfOperation,
        CancellationToken cancellationToken)
    {
        var usersBefore = LedgerLegs.RegisteredUsers(operation, peopleOfOperation);
        var usersAfter = built?.RegisteredUserIds ?? new HashSet<Guid>();
        var involvedUsers = usersBefore.Union(usersAfter).ToList();

        var dates = new List<DateTimeOffset> { operation.OccurredAt };

        if (built is not null)
            dates.Add(built.Operation.OccurredAt);

        await locks.EnsureOpenAsync(involvedUsers, dates, authorUserId, cancellationToken);
        await merges.EnsureNotMergingAsync(
            operation.InvolvedPersonIds.Concat(built?.Operation.InvolvedPersonIds ?? []), cancellationToken);

        var change = OperationChange.Request(
            operation.Id,
            kind,
            authorUserId,
            reason,
            draft is null ? null : JsonSerializer.Serialize(draft, Json),
            built is null ? null : JsonSerializer.Serialize(OperationSnapshot.Of(built.Operation), Json),
            involvedUsers);

        operation.StartChange(change.Id, restoring: kind == ChangeKind.Restore);
        dbContext.Changes.Add(change);

        // Nobody else has to confirm (e.g. the contacts have no account): it is applied right away.
        if (change.AllAnswered)
            await ApplyAsync(change, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return await ResultAsync(change, cancellationToken);
    }

    /// <summary>
    /// Everybody answered: builds the new version and asks the ledger to update the books of the
    /// author and of those who accepted. Those who rejected keep the previous version (disputed).
    /// </summary>
    private async Task ApplyAsync(OperationChange change, CancellationToken cancellationToken)
    {
        var operation = await dbContext.Operations.SingleAsync(o => o.Id == change.OperationId, cancellationToken);
        var peopleOfOperation = await factory.PeopleOfAsync(operation, cancellationToken);

        BuiltOperation? built = null;

        if (change.Kind != ChangeKind.Delete)
        {
            try
            {
                var draft = JsonSerializer.Deserialize<OperationDraft>(change.Draft!, Json)!;
                built = await BuildAsync(operation, draft, requireMyWallet: false, cancellationToken);
            }
            catch (Exception ex) when (ex is DomainException or NotFoundException or NotYetAvailableException)
            {
                // Something changed since it was requested (e.g. a wallet was deleted).
                operation.EndChange();
                change.MarkFailed(ex.Message);
                return;
            }

            var latest = await dbContext.Operations
                .Where(o => o.EntryId == operation.EntryId)
                .MaxAsync(o => o.Revision, cancellationToken);

            built.Operation.BecomeRevisionOf(operation, latest + 1);
            dbContext.Operations.Add(built.Operation);
            dbContext.Debts.AddRange(built.Debts);
        }

        change.StartApplying(built?.Operation.Id);

        var usersBefore = LedgerLegs.RegisteredUsers(operation, peopleOfOperation);
        var withBooks = usersBefore.Union(built?.RegisteredUserIds ?? new HashSet<Guid>());
        var updating = change.AcceptedUserIds.Append(change.RequestedByUserId).Where(withBooks.Contains).ToHashSet();

        if (updating.Count == 0)
        {
            // Nobody involved has books (e.g. a group expense between people without an account).
            await FinishAsync(change, cancellationToken);
            return;
        }

        var versions = await dbContext.Operations
            .Where(o => o.EntryId == operation.EntryId)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        var target = built?.Operation ?? operation;

        await eventBus.PublishAsync(
            new LedgerRevisionRequested(
                change.Id,
                change.Kind switch
                {
                    ChangeKind.Revise => "Edit",
                    ChangeKind.Delete => "Delete",
                    _ => "Restore"
                },
                versions,
                built?.Operation.Id,
                operation.Kind.ToString(),
                target.OccurredAt,
                target.Description,
                target.Currency.ToString(),
                change.RequestedByUserId,
                change.Reason,
                updating.ToList(),
                built?.Legs.Where(l => updating.Contains(l.UserId)).ToList() ?? []),
            cancellationToken);
    }

    /// <summary>The books were updated: the new version replaces the previous one.</summary>
    private async Task FinishAsync(OperationChange change, CancellationToken cancellationToken)
    {
        var operation = await dbContext.Operations.SingleAsync(o => o.Id == change.OperationId, cancellationToken);
        var replacement = change.NewOperationId is { } newId
            ? dbContext.Operations.Local.SingleOrDefault(o => o.Id == newId)
              ?? await dbContext.Operations.SingleAsync(o => o.Id == newId, cancellationToken)
            : null;

        switch (change.Kind)
        {
            case ChangeKind.Revise:
                operation.Supersede();
                break;
            case ChangeKind.Delete:
                operation.MarkDeleted();
                operation.MarkDisputed(change.AnyRejected);
                break;
            case ChangeKind.Restore:
                operation.EndChange();
                break;
        }

        if (change.Kind != ChangeKind.Restore)
        {
            foreach (var debt in await lifecycle.DebtsOfAsync(operation.Id, cancellationToken))
                debt.Supersede();
        }

        if (operation.Kind == OperationKind.Settlement)
        {
            foreach (var debt in await lifecycle.SettledByAsync(operation.Id, cancellationToken))
            {
                if (replacement is null)
                    debt.Reopen(operation.Id);
                else
                    debt.MoveSettlement(operation.Id, replacement.Id);
            }
        }

        if (replacement is not null)
        {
            replacement.Complete();
            replacement.MarkDisputed(change.AnyRejected);

            foreach (var debt in await lifecycle.DebtsOfAsync(replacement.Id, cancellationToken))
                debt.Activate();

            await lifecycle.PublishCompletedAsync(replacement, cancellationToken);
        }

        change.MarkApplied();
    }

    /// <summary>The ledger refused it (e.g. not enough money): everything stays as it was.</summary>
    private async Task FailAsync(OperationChange change, string reason, CancellationToken cancellationToken)
    {
        var operation = await dbContext.Operations.SingleAsync(o => o.Id == change.OperationId, cancellationToken);
        operation.EndChange();

        if (change.NewOperationId is { } newId)
        {
            var replacement = await dbContext.Operations.SingleAsync(o => o.Id == newId, cancellationToken);
            replacement.Reject(reason);

            foreach (var debt in await lifecycle.DebtsOfAsync(newId, cancellationToken))
                debt.Reject();
        }

        change.MarkFailed(reason);
    }

    private async Task<DebtOperation> LoadAsync(Guid operationId, CancellationToken cancellationToken)
        => await dbContext.Operations.SingleOrDefaultAsync(o => o.Id == operationId, cancellationToken)
           ?? throw new NotFoundException("Operation", operationId);

    /// <summary>Any registered user involved may change it; for group operations, any member of the group.</summary>
    private async Task EnsureMayChangeAsync(
        DebtOperation operation,
        Guid userId,
        IReadOnlyDictionary<Guid, PersonInfo> peopleOfOperation,
        CancellationToken cancellationToken)
    {
        var involved = LedgerLegs.RegisteredUsers(operation, peopleOfOperation).Contains(userId)
                       || (operation.GroupId is { } groupId && await dbContext.GroupMembers.AnyAsync(
                           m => m.GroupId == groupId && m.UserId == userId, cancellationToken));

        if (!involved)
            throw new NotFoundException("Operation", operation.Id);
    }

    /// <summary>Settled debts cannot change: the settlement has to be undone (deleted) first.</summary>
    private async Task EnsureNotPaidAsync(DebtOperation operation, CancellationToken cancellationToken)
    {
        if (operation.Kind == OperationKind.Settlement)
            return;

        var paid = await dbContext.Debts.AnyAsync(
            d => d.OperationId == operation.Id
                 && (d.Status == DebtStatus.Settled || (d.Status == DebtStatus.Open && d.SettlementOperationId != null)),
            cancellationToken);

        if (paid)
            throw new DomainException(
                "debt.settled",
                "It was already settled. Undo (delete) the settlement first, then change it.");
    }

    /// <summary>The requested values, keeping what only the initiator may change.</summary>
    private async Task<OperationDraft> DraftFromAsync(
        DebtOperation operation,
        ReviseOperationCommand command,
        CancellationToken cancellationToken)
    {
        var isInitiator = command.UserId == operation.InitiatorUserId;
        var previous = DraftOf(operation);

        string Currency()
        {
            if (command.Currency is null || command.Currency == previous.Currency)
                return previous.Currency;

            if (!isInitiator)
                throw new DomainException(
                    "operation.initiator_only",
                    "Only the person who recorded it can change the currency.");

            return command.Currency;
        }

        async Task<Guid?> CounterpartyAsync()
            => isInitiator && command.ContactId is { } contactId
                ? (await people.ContactAsync(command.UserId, contactId, cancellationToken)).PersonId
                : previous.CounterpartyPersonId;

        decimal Amount() => command.Amount
                            ?? throw new DomainException("operation.amount_required", "Enter the amount.");

        var common = previous with
        {
            OccurredAt = command.OccurredAt,
            Description = command.Description,
            RecordAs = null
        };

        switch (operation.Kind)
        {
            case OperationKind.Loan:
                return common with
                {
                    Amount = Amount(),
                    CounterpartyPersonId = await CounterpartyAsync(),
                    WalletId = isInitiator ? command.WalletId ?? previous.WalletId : previous.WalletId
                };

            case OperationKind.InitialDebt:
                return common with
                {
                    Amount = Amount(),
                    Currency = Currency(),
                    CounterpartyPersonId = await CounterpartyAsync(),
                    Direction = isInitiator ? command.Direction ?? previous.Direction : previous.Direction
                };

            case OperationKind.Split:
            {
                if (command.Payers is null || command.Participants is null || command.Method is null)
                    throw new DomainException("split.incomplete", "Enter the payers, the participants and how to split.");

                var myPreviousWallet = operation.Payers.FirstOrDefault(p => p.PersonId == operation.InitiatorUserId)?.WalletId;

                var payers = command.Payers
                    .Select(p => p.PersonId != operation.InitiatorUserId
                        ? p with { WalletId = null }
                        : isInitiator ? p : p with { WalletId = myPreviousWallet })
                    .ToList();

                return common with
                {
                    Amount = Amount(),
                    Currency = Currency(),
                    Method = command.Method,
                    Payers = payers,
                    Participants = command.Participants,
                    CategoryId = isInitiator ? command.CategoryId : previous.CategoryId
                };
            }

            case OperationKind.Settlement:
                return common with
                {
                    WalletId = isInitiator ? command.WalletId ?? previous.WalletId : previous.WalletId,
                    Description = command.Description ?? operation.Description
                };

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    /// <summary>The operation as it is, as a draft (used to restore it and as the base for edits).</summary>
    private static OperationDraft DraftOf(DebtOperation operation)
    {
        var draft = new OperationDraft(
            operation.OccurredAt, operation.Description, operation.Total, operation.Currency.ToString());

        return operation.Kind switch
        {
            OperationKind.Loan => draft with
            {
                CounterpartyPersonId = operation.ToPersonId,
                WalletId = operation.Payers.Single().WalletId
            },
            OperationKind.InitialDebt => draft with
            {
                // From: the debtor, To: the creditor.
                CounterpartyPersonId = operation.FromPersonId == operation.InitiatorUserId ? operation.ToPersonId : operation.FromPersonId,
                Direction = operation.FromPersonId == operation.InitiatorUserId ? InitialDebtDirection.IOwe : InitialDebtDirection.TheyOweMe
            },
            OperationKind.Split => draft with
            {
                Method = SplitMethod.Custom,
                RecordAs = operation.Method,
                Payers = operation.Payers.Select(p => new SplitPayer(p.PersonId, p.Amount, p.WalletId)).ToList(),
                Participants = operation.Shares.Select(s => new SplitParticipant(s.PersonId, s.Amount)).ToList(),
                CategoryId = operation.CategoryId
            },
            OperationKind.Settlement => draft with { WalletId = operation.WalletId },
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    /// <summary>Builds the new version from the initiator's point of view.</summary>
    private async Task<BuiltOperation> BuildAsync(
        DebtOperation operation,
        OperationDraft draft,
        bool requireMyWallet,
        CancellationToken cancellationToken)
    {
        var initiator = operation.InitiatorUserId;
        var currency = Currencies.Parse(draft.Currency);

        switch (operation.Kind)
        {
            case OperationKind.Loan:
            {
                var borrower = await PersonAsync(operation, draft.CounterpartyPersonId!.Value, cancellationToken);

                return await factory.LoanAsync(
                    initiator, borrower, draft.WalletId!.Value, draft.Amount, draft.OccurredAt, draft.Description,
                    cancellationToken);
            }

            case OperationKind.InitialDebt:
            {
                var contact = await PersonAsync(operation, draft.CounterpartyPersonId!.Value, cancellationToken);

                return await factory.InitialDebtAsync(
                    initiator, contact, draft.Direction!.Value, draft.Amount, currency, draft.OccurredAt,
                    draft.Description, cancellationToken);
            }

            case OperationKind.Split:
                return await factory.SplitAsync(
                    initiator, operation.GroupId, draft.Amount, currency, draft.Method!.Value, draft.Payers!,
                    draft.Participants!, draft.CategoryId, draft.OccurredAt, draft.Description, requireMyWallet,
                    cancellationToken, draft.RecordAs);

            case OperationKind.Settlement when operation.GroupId is { } groupId:
            {
                var members = await factory.GroupMembersAsync(groupId, cancellationToken);
                var from = members.GetValueOrDefault(operation.FromPersonId!.Value)
                           ?? throw new DomainException("settlement.not_member", "The payer is no longer a member of the group.");
                var to = members.GetValueOrDefault(operation.ToPersonId!.Value)
                         ?? throw new DomainException("settlement.not_member", "The receiver is no longer a member of the group.");

                return await factory.GroupSettlementAsync(
                    initiator, groupId, from, to, operation.Total, currency, draft.WalletId, draft.OccurredAt,
                    draft.Description, cancellationToken, requireMyWallet);
            }

            case OperationKind.Settlement:
            {
                var otherId = operation.FromPersonId == initiator ? operation.ToPersonId!.Value : operation.FromPersonId!.Value;
                var other = await PersonAsync(operation, otherId, cancellationToken);
                var debts = await lifecycle.SettledByAsync(operation.Id, cancellationToken);

                return await factory.DebtSettlementAsync(
                    initiator, operation.Scope!.Value, other, debts, currency, draft.WalletId, draft.OccurredAt,
                    draft.Description, cancellationToken, requireMyWallet);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private async Task<PersonInfo> PersonAsync(DebtOperation operation, Guid personId, CancellationToken cancellationToken)
    {
        var circle = await people.CircleAsync(operation.InitiatorUserId, cancellationToken);

        return circle.GetValueOrDefault(personId)
               ?? throw new DomainException("operation.unknown_person", "That person is no longer a contact of whoever recorded it.");
    }

    private async Task<ChangeResultDto> ResultAsync(OperationChange change, CancellationToken cancellationToken)
    {
        var waitingIds = change.Approvals.Where(a => a.Status == ApprovalStatus.Pending).Select(a => a.UserId).ToList();

        var names = await dbContext.Users.AsNoTracking()
            .Where(u => waitingIds.Contains(u.UserId))
            .Select(u => u.DisplayName)
            .ToListAsync(cancellationToken);

        return new ChangeResultDto(change.Id, change.OperationId, change.Kind, change.Status, change.NewOperationId, names);
    }
}
