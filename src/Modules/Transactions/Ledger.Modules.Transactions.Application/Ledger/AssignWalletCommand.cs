using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Transactions.Domain;

namespace Ledger.Modules.Transactions.Application.Ledger;

/// <summary>Moves money waiting in "pending settlements" into (or out of) a real wallet.</summary>
/// <param name="OccurredAt">When the money reached or left the wallet (default: the date of the operation).</param>
public sealed record AssignWalletCommand(Guid UserId, Guid AssignmentId, Guid WalletId, DateTimeOffset? OccurredAt = null)
    : ICommand<Guid>;

internal sealed class AssignWalletValidator : AbstractValidator<AssignWalletCommand>
{
    public AssignWalletValidator()
    {
        RuleFor(x => x.OccurredAt).NotInFuture();
    }
}

internal sealed class AssignWalletHandler(
    ITransactionsDbContext dbContext,
    AccountResolver accounts,
    LedgerWriter writer)
    : ICommandHandler<AssignWalletCommand, Guid>
{
    public async Task<Guid> Handle(AssignWalletCommand command, CancellationToken cancellationToken)
    {
        return await dbContext.InTransactionAsync(async () =>
        {
            var assignment = await dbContext.LockAssignmentAsync(command.AssignmentId, cancellationToken);

            if (assignment is null || assignment.OwnerId != command.UserId)
                throw new NotFoundException("Pending assignment", command.AssignmentId);

            var wallet = await accounts.WalletAsync(command.UserId, command.WalletId, cancellationToken);

            if (wallet.Currency != assignment.Currency)
                throw new DomainException(
                    "assignment.currency_mismatch",
                    $"The amount is in {assignment.Currency}; choose a {assignment.Currency} wallet.");

            var pending = await accounts.SystemAsync(
                command.UserId, LedgerAccountPurpose.PendingSettlements, cancellationToken);

            assignment.Assign(command.WalletId);

            var transaction = TransactionBuilder
                .Create(
                    command.UserId,
                    command.OccurredAt ?? assignment.OccurredAt,
                    TransactionKind.WalletAssignment,
                    assignment.Description,
                    assignment.OperationId)
                .Add(wallet.Id, assignment.Amount, assignment.Currency)
                .Add(pending.Id, -assignment.Amount, assignment.Currency, assignment.CounterpartyId)
                .Build();

            await writer.WriteAsync([transaction], cancellationToken);

            return transaction.Id.Value;
        }, cancellationToken);
    }
}
