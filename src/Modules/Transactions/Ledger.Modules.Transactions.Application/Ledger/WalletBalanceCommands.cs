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
/// current opening entry is reversed and a new one is recorded.
/// </summary>
public sealed record ChangeOpeningBalanceCommand(Guid UserId, Guid WalletId, decimal Amount, DateTimeOffset OccurredAt)
    : ICommand;

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
        var opening = await ActiveOpeningAsync(query.WalletId, cancellationToken);

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
            var current = await ActiveOpeningAsync(command.WalletId, cancellationToken);
            var transactions = new List<Transaction>();

            // The new entry goes first: the overdraft check then only fails if the final balance is negative.
            if (command.Amount > 0)
            {
                var openingBalances = await accounts.SystemAsync(
                    command.UserId, LedgerAccountPurpose.OpeningBalances, cancellationToken);

                transactions.Add(TransactionBuilder
                    .Create(command.UserId, command.OccurredAt, TransactionKind.OpeningBalance, "Opening balance", command.WalletId)
                    .Add(wallet.Id, command.Amount, currency)
                    .Add(openingBalances.Id, -command.Amount, currency)
                    .Build());
            }

            if (current is not null)
            {
                var reversal = TransactionBuilder.Create(
                    command.UserId,
                    current.OccurredAt,
                    TransactionKind.OpeningBalanceReversal,
                    "Opening balance replaced",
                    current.Id.Value);

                foreach (var posting in current.Postings)
                    reversal.Add(posting.AccountId, -posting.Amount, posting.Currency, posting.CounterpartyId);

                transactions.Add(reversal.Build());
            }

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
    private async Task<Transaction?> ActiveOpeningAsync(Guid walletId, CancellationToken cancellationToken)
    {
        var openings = await dbContext.Transactions
            .AsNoTracking()
            .Include(t => t.Postings)
            .Where(t => t.Kind == TransactionKind.OpeningBalance && t.CorrelationId == walletId)
            .ToListAsync(cancellationToken);

        if (openings.Count == 0)
            return null;

        var ids = openings.Select(o => (Guid?)o.Id.Value).ToList();

        var reversed = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Kind == TransactionKind.OpeningBalanceReversal && ids.Contains(t.CorrelationId))
            .Select(t => t.CorrelationId)
            .ToListAsync(cancellationToken);

        return openings
            .Where(o => !reversed.Contains(o.Id.Value))
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefault();
    }
}
