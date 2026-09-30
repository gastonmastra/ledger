using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Ledger;

public sealed record OpeningBalanceDto(decimal Amount, DateTimeOffset? OccurredAt);

public sealed record GetOpeningBalanceQuery(Guid UserId, Guid WalletId) : IQuery<OpeningBalanceDto>;

/// <summary>
/// Replaces the opening balance of a wallet (amount and date). The ledger is append-only: the
/// current opening entry is reversed and a new revision is recorded.
/// </summary>
public sealed record ChangeOpeningBalanceCommand(
    Guid UserId,
    Guid WalletId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string Reason) : ICommand;

/// <summary>Reconciles a wallet with its real balance; the difference goes to "Balance adjustments".</summary>
public sealed record AdjustBalanceCommand(
    Guid UserId,
    Guid WalletId,
    decimal ActualBalance,
    DateTimeOffset OccurredAt,
    string? Description) : ICommand<Guid>;

internal sealed class ChangeOpeningBalanceValidator : AbstractValidator<ChangeOpeningBalanceCommand>
{
    public ChangeOpeningBalanceValidator()
    {
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).PrecisionScale(19, Money.Decimals, true);
        RuleFor(x => x.OccurredAt).NotInFuture();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(Transaction.ReasonMaxLength);
    }
}

internal sealed class AdjustBalanceValidator : AbstractValidator<AdjustBalanceCommand>
{
    public AdjustBalanceValidator()
    {
        RuleFor(x => x.ActualBalance).GreaterThanOrEqualTo(0).PrecisionScale(19, Money.Decimals, true);
        RuleFor(x => x.OccurredAt).NotInFuture();
        RuleFor(x => x.Description).MaximumLength(Transaction.DescriptionMaxLength);
    }
}

internal sealed class WalletBalanceHandlers(
    ITransactionsDbContext dbContext,
    AccountResolver accounts,
    LedgerWriter writer)
    : IQueryHandler<GetOpeningBalanceQuery, OpeningBalanceDto>,
      ICommandHandler<ChangeOpeningBalanceCommand>,
      ICommandHandler<AdjustBalanceCommand, Guid>
{
    public async Task<OpeningBalanceDto> Handle(GetOpeningBalanceQuery query, CancellationToken cancellationToken)
    {
        var wallet = await accounts.WalletAsync(query.UserId, query.WalletId, cancellationToken);
        var opening = await ActiveOpeningAsync(query.WalletId, tracked: false, cancellationToken);

        return opening is null
            ? new OpeningBalanceDto(0, null)
            : new OpeningBalanceDto(
                opening.Postings.Where(p => p.AccountId == wallet.Id).Sum(p => p.Amount),
                opening.OccurredAt);
    }

    public async Task Handle(ChangeOpeningBalanceCommand command, CancellationToken cancellationToken)
    {
        var wallet = await accounts.WalletAsync(command.UserId, command.WalletId, cancellationToken);
        var currency = wallet.Currency!.Value;

        await dbContext.InTransactionAsync(async () =>
        {
            var current = await ActiveOpeningAsync(command.WalletId, tracked: true, cancellationToken);
            var transactions = new List<Transaction>();

            if (current is not null)
                transactions.Add(Transaction.ReversalOf(current, command.UserId, ChangeAction.Edit, command.Reason));

            if (command.Amount > 0)
            {
                var openingBalances = await accounts.SystemAsync(
                    command.UserId, LedgerAccountPurpose.OpeningBalances, cancellationToken);

                var opening = TransactionBuilder
                    .Create(command.UserId, command.OccurredAt, TransactionKind.OpeningBalance, "Opening balance", command.WalletId)
                    .Add(wallet.Id, command.Amount, currency)
                    .Add(openingBalances.Id, -command.Amount, currency)
                    .Build();

                if (current is not null)
                    opening.BecomeRevisionOf(current, current.Revision + 1, command.UserId, ChangeAction.Edit, command.Reason);

                transactions.Add(opening);
            }

            // The balance check applies to the final result of the whole batch.
            if (transactions.Count != 0)
                await writer.WriteAsync(transactions, cancellationToken);

            return true;
        }, cancellationToken);
    }

    public async Task<Guid> Handle(AdjustBalanceCommand command, CancellationToken cancellationToken)
    {
        var wallet = await accounts.WalletAsync(command.UserId, command.WalletId, cancellationToken);
        var adjustments = await accounts.SystemAsync(
            command.UserId, LedgerAccountPurpose.BalanceAdjustments, cancellationToken);
        var currency = wallet.Currency!.Value;

        return await dbContext.InTransactionAsync(async () =>
        {
            // Read the balance under the row lock so a concurrent posting cannot change it meanwhile.
            var locked = (await dbContext.LockAccountsAsync([wallet.Id], cancellationToken)).Single();
            var difference = command.ActualBalance - locked.Balance;

            if (difference == 0)
                throw new DomainException(
                    "adjustment.no_difference",
                    "The wallet already has that balance: there is nothing to adjust.");

            var transaction = TransactionBuilder
                .Create(
                    command.UserId,
                    command.OccurredAt,
                    TransactionKind.BalanceAdjustment,
                    command.Description ?? "Balance adjustment",
                    command.WalletId)
                .Add(wallet.Id, difference, currency)
                .Add(adjustments.Id, -difference, currency)
                .Build();

            await writer.WriteAsync([transaction], cancellationToken);

            return transaction.Id.Value;
        }, cancellationToken);
    }

    /// <summary>The opening entry of the wallet that has not been replaced.</summary>
    private async Task<Transaction?> ActiveOpeningAsync(Guid walletId, bool tracked, CancellationToken cancellationToken)
    {
        var query = dbContext.Transactions
            .Include(t => t.Postings)
            .Where(t => t.Kind == TransactionKind.OpeningBalance && t.CorrelationId == walletId && t.ReversedById == null);

        if (!tracked)
            query = query.AsNoTracking();

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
