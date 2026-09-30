using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Transactions.Contracts;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Ledger;

/// <summary>Records the opening balance of a wallet whose account was just created. Idempotent.</summary>
public sealed record PostOpeningBalanceCommand(Guid AccountId, decimal Amount, DateTimeOffset OccurredAt) : ICommand;

public sealed record LedgerPostingResult(bool Succeeded, string? RejectionReason);

/// <summary>Records a debt operation (loan, split, settlement) in the books of every user involved.</summary>
public sealed record PostDebtOperationCommand(LedgerPostingRequested Request) : ICommand<LedgerPostingResult>;

internal sealed class PostingHandlers(
    ITransactionsDbContext dbContext,
    AccountResolver accounts,
    LedgerWriter writer)
    : ICommandHandler<PostOpeningBalanceCommand>,
      ICommandHandler<PostDebtOperationCommand, LedgerPostingResult>
{
    public async Task Handle(PostOpeningBalanceCommand command, CancellationToken cancellationToken)
    {
        if (command.Amount <= 0)
            return;

        await dbContext.InTransactionAsync(async () =>
        {
            var wallet = await dbContext.LedgerAccounts
                .AsNoTracking()
                .SingleAsync(x => x.Id == new AccountId(command.AccountId), cancellationToken);

            var alreadyPosted = await dbContext.Transactions.AnyAsync(
                x => x.Kind == TransactionKind.OpeningBalance && x.CorrelationId == wallet.ReferenceId,
                cancellationToken);

            if (alreadyPosted)
                return true;

            var openingBalances = await accounts.SystemAsync(
                wallet.OwnerId, LedgerAccountPurpose.OpeningBalances, cancellationToken);
            var currency = wallet.Currency!.Value;

            var transaction = TransactionBuilder
                .Create(wallet.OwnerId, command.OccurredAt, TransactionKind.OpeningBalance, "Opening balance", wallet.ReferenceId)
                .Add(wallet.Id, command.Amount, currency)
                .Add(openingBalances.Id, -command.Amount, currency)
                .Build();

            await writer.WriteAsync([transaction], cancellationToken);

            return true;
        }, cancellationToken);
    }

    public async Task<LedgerPostingResult> Handle(PostDebtOperationCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        var alreadyPosted = await dbContext.Transactions.AnyAsync(
            x => x.CorrelationId == request.OperationId && x.Kind != TransactionKind.WalletAssignment,
            cancellationToken);

        if (alreadyPosted)
            return new LedgerPostingResult(true, null);

        try
        {
            return await dbContext.InTransactionAsync(async () =>
            {
                var currency = Currencies.Parse(request.Currency);
                var kind = Enum.Parse<TransactionKind>(request.Kind);
                var transactions = new List<Transaction>();

                foreach (var leg in request.Legs)
                    transactions.Add(await BuildLegAsync(request, leg, kind, currency, cancellationToken));

                await writer.WriteAsync(transactions, cancellationToken);

                return new LedgerPostingResult(true, null);
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException)
        {
            // Business rejection (e.g. not enough money): nothing is written, the requester is told why.
            dbContext.DiscardLedgerChanges();
            return new LedgerPostingResult(false, ex.Message);
        }
    }

    private async Task<Transaction> BuildLegAsync(
        LedgerPostingRequested request,
        LedgerLeg leg,
        TransactionKind kind,
        Currency currency,
        CancellationToken cancellationToken)
    {
        var builder = TransactionBuilder.Create(
            leg.UserId, request.OccurredAt, kind, request.Description, request.OperationId);

        if (leg.CashAmount != 0)
        {
            if (leg.WalletId is { } walletId)
            {
                var wallet = await accounts.WalletAsync(leg.UserId, walletId, cancellationToken);

                if (wallet.Currency != currency)
                    throw new DomainException(
                        "ledger.currency_mismatch",
                        $"Wallet '{wallet.Name}' holds {wallet.Currency}; the operation is in {currency}.");

                builder.Add(wallet.Id, leg.CashAmount, currency);
            }
            else
            {
                // The user did not choose the wallet (somebody else recorded the operation).
                var pending = await accounts.SystemAsync(
                    leg.UserId, LedgerAccountPurpose.PendingSettlements, cancellationToken);
                var counterpartyId = leg.Positions.FirstOrDefault()?.CounterpartyId;

                builder.Add(pending.Id, leg.CashAmount, currency, counterpartyId);

                dbContext.PendingWalletAssignments.Add(PendingWalletAssignment.Create(
                    leg.UserId,
                    request.OperationId,
                    leg.CashAmount,
                    currency,
                    counterpartyId,
                    request.Description,
                    request.OccurredAt));
            }
        }

        if (leg.ExpenseAmount != 0)
        {
            var category = await accounts.CategoryAsync(
                leg.UserId, leg.CategoryId, LedgerAccountType.Expense, cancellationToken);

            builder.Add(category.Id, leg.ExpenseAmount, currency);
        }

        if (leg.Positions.Count != 0)
        {
            var receivables = await accounts.SystemAsync(leg.UserId, LedgerAccountPurpose.Receivables, cancellationToken);
            var payables = await accounts.SystemAsync(leg.UserId, LedgerAccountPurpose.Payables, cancellationToken);

            foreach (var position in leg.Positions)
            {
                var account = position.Side == PositionSides.Receivable ? receivables : payables;
                builder.Add(account.Id, position.Amount, currency, position.CounterpartyId);
            }
        }

        return builder.Build();
    }
}
