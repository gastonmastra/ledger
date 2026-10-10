using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Ledger;

/// <summary>
/// Corrects an income, expense or transfer: the current revision is reversed (same date) and the
/// corrected one is recorded as the next revision of the same entry.
/// </summary>
/// <param name="WalletId">The wallet (for a transfer: the source wallet).</param>
/// <param name="ToWalletId">Transfers only: the destination wallet.</param>
public sealed record ReviseMovementCommand(
    Guid UserId,
    Guid TransactionId,
    Guid WalletId,
    Guid? ToWalletId,
    Guid? CategoryId,
    decimal Amount,
    decimal? ReceivedAmount,
    DateTimeOffset OccurredAt,
    string? Description,
    string Reason) : ICommand<Guid>;

/// <summary>Deletes an income, expense, transfer or balance adjustment by posting its reversal.</summary>
public sealed record DeleteTransactionCommand(Guid UserId, Guid TransactionId, string Reason) : ICommand;

/// <summary>Brings back the last revision of a deleted entry.</summary>
public sealed record RestoreTransactionCommand(Guid UserId, Guid TransactionId, string Reason) : ICommand<Guid>;

internal sealed class ReviseMovementValidator : AbstractValidator<ReviseMovementCommand>
{
    public ReviseMovementValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(19, Money.Decimals, true);
        RuleFor(x => x.ReceivedAmount).GreaterThan(0).PrecisionScale(19, Money.Decimals, true)
            .When(x => x.ReceivedAmount is not null);
        RuleFor(x => x.Description).MaximumLength(Transaction.DescriptionMaxLength);
        RuleFor(x => x.OccurredAt).NotInFuture();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(Transaction.ReasonMaxLength);
    }
}

internal sealed class DeleteTransactionValidator : AbstractValidator<DeleteTransactionCommand>
{
    public DeleteTransactionValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(Transaction.ReasonMaxLength);
    }
}

internal sealed class RestoreTransactionValidator : AbstractValidator<RestoreTransactionCommand>
{
    public RestoreTransactionValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(Transaction.ReasonMaxLength);
    }
}

internal sealed class RevisionHandlers(
    ITransactionsDbContext dbContext,
    MovementFactory movements,
    LedgerWriter writer)
    : ICommandHandler<ReviseMovementCommand, Guid>,
      ICommandHandler<DeleteTransactionCommand>,
      ICommandHandler<RestoreTransactionCommand, Guid>
{
    /// <summary>Kinds the user records directly (debt operations are changed through Debts).</summary>
    public static readonly TransactionKind[] Editable = [TransactionKind.Income, TransactionKind.Expense, TransactionKind.Transfer];

    public static readonly TransactionKind[] Deletable =
        [TransactionKind.Income, TransactionKind.Expense, TransactionKind.Transfer, TransactionKind.BalanceAdjustment];

    public async Task<Guid> Handle(ReviseMovementCommand command, CancellationToken cancellationToken)
    {
        return await dbContext.InTransactionAsync(async () =>
        {
            var original = await GetOwnedAsync(command.UserId, command.TransactionId, cancellationToken);
            EnsureCurrent(original);

            if (!Editable.Contains(original.Kind))
                throw new DomainException(
                    "transaction.not_editable",
                    "This kind of transaction is changed from where it was created (wallet opening balance, debts…).");

            var corrected = original.Kind switch
            {
                TransactionKind.Income => await movements.IncomeAsync(
                    command.UserId, command.WalletId, command.CategoryId, command.Amount, command.OccurredAt,
                    command.Description, cancellationToken),
                TransactionKind.Expense => await movements.ExpenseAsync(
                    command.UserId, command.WalletId, command.CategoryId, command.Amount, command.OccurredAt,
                    command.Description, cancellationToken),
                _ => await movements.TransferAsync(
                    command.UserId, command.WalletId,
                    command.ToWalletId ?? throw new DomainException("transfer.destination_required", "Choose the destination wallet."),
                    command.Amount, command.ReceivedAmount, command.OccurredAt, command.Description, cancellationToken)
            };

            corrected.BecomeRevisionOf(original, original.Revision + 1, command.UserId, ChangeAction.Edit, command.Reason);
            var reversal = Transaction.ReversalOf(original, command.UserId, ChangeAction.Edit, command.Reason);

            await writer.WriteAsync([reversal, corrected], cancellationToken);

            return corrected.Id.Value;
        }, cancellationToken);
    }

    public async Task Handle(DeleteTransactionCommand command, CancellationToken cancellationToken)
    {
        await dbContext.InTransactionAsync(async () =>
        {
            var original = await GetOwnedAsync(command.UserId, command.TransactionId, cancellationToken);
            EnsureCurrent(original);

            if (!Deletable.Contains(original.Kind))
                throw new DomainException(
                    "transaction.not_deletable",
                    "This kind of transaction is deleted from where it was created (wallet opening balance, debts…).");

            var reversal = Transaction.ReversalOf(original, command.UserId, ChangeAction.Delete, command.Reason);
            await writer.WriteAsync([reversal], cancellationToken);

            return true;
        }, cancellationToken);
    }

    public async Task<Guid> Handle(RestoreTransactionCommand command, CancellationToken cancellationToken)
    {
        return await dbContext.InTransactionAsync(async () =>
        {
            var target = await GetOwnedAsync(command.UserId, command.TransactionId, cancellationToken);

            var entry = await dbContext.Transactions
                .Include(t => t.Postings)
                .Where(t => t.EntryId == target.EntryId && t.OwnerId == command.UserId)
                .ToListAsync(cancellationToken);

            if (entry.Any(t => t.IsCurrent))
                throw new DomainException("transaction.not_deleted", "This transaction is not deleted.");

            var last = entry
                .Where(t => !t.IsReversal)
                .OrderByDescending(t => t.Revision)
                .First();

            if (!Deletable.Contains(last.Kind))
                throw new DomainException(
                    "transaction.not_restorable", "This kind of transaction is restored from where it was created.");

            var restored = TransactionBuilder.Create(
                last.OwnerId, last.OccurredAt, last.Kind, last.Description, last.CorrelationId);

            foreach (var posting in last.Postings)
                restored.Add(posting.AccountId, posting.Amount, posting.Currency, posting.CounterpartyId);

            var transaction = restored.Build();
            transaction.BecomeRevisionOf(
                last, entry.Max(t => t.Revision) + 1, command.UserId, ChangeAction.Restore, command.Reason);

            await writer.WriteAsync([transaction], cancellationToken);

            return transaction.Id.Value;
        }, cancellationToken);
    }

    private async Task<Transaction> GetOwnedAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken)
    {
        var id = new TransactionId(transactionId);
        var transaction = await dbContext.Transactions
            .Include(t => t.Postings)
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (transaction is null || transaction.OwnerId != userId)
            throw new NotFoundException("Transaction", transactionId);

        return transaction;
    }

    private static void EnsureCurrent(Transaction transaction)
    {
        if (!transaction.IsCurrent)
            throw new DomainException(
                "transaction.not_current",
                "This version was already changed or deleted. Open its history to see the current one.");
    }
}
