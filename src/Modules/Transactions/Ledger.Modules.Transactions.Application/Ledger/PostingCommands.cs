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

/// <summary>Applies a change of a shared operation to the books of the users who accepted it.</summary>
public sealed record ReviseDebtOperationCommand(LedgerRevisionRequested Request) : ICommand<LedgerPostingResult>;

/// <summary>Copies the shared history into the books of a user who was just linked to a contact.</summary>
public sealed record PostLedgerOpeningsCommand(LedgerOpeningsRequested Request) : ICommand<LedgerPostingResult>;

internal readonly record struct PostingContext(Guid OperationId, DateTimeOffset OccurredAt, string? Description);

internal sealed class PostingHandlers(
    ITransactionsDbContext dbContext,
    AccountResolver accounts,
    LedgerWriter writer)
    : ICommandHandler<PostOpeningBalanceCommand>,
      ICommandHandler<PostDebtOperationCommand, LedgerPostingResult>,
      ICommandHandler<ReviseDebtOperationCommand, LedgerPostingResult>,
      ICommandHandler<PostLedgerOpeningsCommand, LedgerPostingResult>
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

                var context = new PostingContext(request.OperationId, request.OccurredAt, request.Description);

                foreach (var leg in request.Legs)
                    transactions.Add(await BuildLegAsync(context, leg, kind, currency, cancellationToken));

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

    public async Task<LedgerPostingResult> Handle(ReviseDebtOperationCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        try
        {
            return await dbContext.InTransactionAsync(async () =>
            {
                var currency = Currencies.Parse(request.Currency);
                var kind = Enum.Parse<TransactionKind>(request.Kind);
                var action = Enum.Parse<ChangeAction>(request.Action);
                var transactions = new List<Transaction>();

                foreach (var userId in request.UserIds.Distinct())
                {
                    // Current transactions of the previous version in this user's books (incl. a wallet assignment).
                    var previous = await dbContext.Transactions
                        .Include(t => t.Postings)
                        .Where(t => t.OwnerId == userId
                                    && t.CorrelationId != null
                                    && request.PreviousOperationIds.Contains(t.CorrelationId.Value)
                                    && t.ReversedById == null
                                    && t.Kind != TransactionKind.Reversal)
                        .OrderBy(t => t.CreatedAt)
                        .ToListAsync(cancellationToken);

                    foreach (var transaction in previous)
                    {
                        transactions.Add(Transaction.ReversalOf(
                            transaction, request.ChangedByUserId, action, request.Reason));
                    }

                    var openAssignments = await dbContext.PendingWalletAssignments
                        .Where(a => a.OwnerId == userId
                                    && request.PreviousOperationIds.Contains(a.OperationId)
                                    && a.Status == PendingWalletAssignmentStatus.Pending)
                        .ToListAsync(cancellationToken);

                    foreach (var assignment in openAssignments)
                        assignment.Cancel();

                    var leg = request.Legs.FirstOrDefault(l => l.UserId == userId);

                    if (leg is not null && request.NewOperationId is { } newOperationId)
                    {
                        var context = new PostingContext(newOperationId, request.OccurredAt, request.Description);
                        var replacement = await BuildLegAsync(context, leg, kind, currency, cancellationToken);
                        // Link to the latest version in this user's books; when it was deleted (a restore),
                        // to the last one that was reversed.
                        var main = previous.LastOrDefault(t => t.Kind != TransactionKind.WalletAssignment)
                                   ?? await dbContext.Transactions
                                       .Where(t => t.OwnerId == userId
                                                   && t.CorrelationId != null
                                                   && request.PreviousOperationIds.Contains(t.CorrelationId.Value)
                                                   && t.Kind != TransactionKind.Reversal
                                                   && t.Kind != TransactionKind.WalletAssignment)
                                       .OrderByDescending(t => t.Revision).ThenByDescending(t => t.CreatedAt)
                                       .FirstOrDefaultAsync(cancellationToken);

                        if (main is not null)
                        {
                            replacement.BecomeRevisionOf(
                                main, main.Revision + 1, request.ChangedByUserId, action, request.Reason);
                        }

                        transactions.Add(replacement);
                    }
                }

                if (transactions.Count != 0)
                    await writer.WriteAsync(transactions, cancellationToken);

                return new LedgerPostingResult(true, null);
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException)
        {
            dbContext.DiscardLedgerChanges();
            return new LedgerPostingResult(false, ex.Message);
        }
    }

    /// <summary>
    /// One existing-debt entry per copied operation, dated like the operation and correlated to it, so a later
    /// revision of that operation reverses the copy too. All of them or none.
    /// </summary>
    public async Task<LedgerPostingResult> Handle(PostLedgerOpeningsCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        if (request.Entries.Count == 0)
            return new LedgerPostingResult(true, null);

        var first = request.Entries[0].OperationId;
        var alreadyPosted = await dbContext.Transactions.AnyAsync(
            x => x.OwnerId == request.UserId && x.CorrelationId == first && x.Kind == TransactionKind.InitialDebt,
            cancellationToken);

        if (alreadyPosted)
            return new LedgerPostingResult(true, null);

        try
        {
            return await dbContext.InTransactionAsync(async () =>
            {
                var transactions = new List<Transaction>();

                foreach (var entry in request.Entries)
                {
                    var context = new PostingContext(entry.OperationId, entry.OccurredAt, entry.Description);
                    transactions.Add(await BuildLegAsync(
                        context, entry.Leg, TransactionKind.InitialDebt, Currencies.Parse(entry.Currency), cancellationToken));
                }

                await writer.WriteAsync(transactions, cancellationToken);

                return new LedgerPostingResult(true, null);
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException)
        {
            dbContext.DiscardLedgerChanges();
            return new LedgerPostingResult(false, ex.Message);
        }
    }

    private async Task<Transaction> BuildLegAsync(
        PostingContext request,
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

        if (leg.OpeningBalanceAmount != 0)
        {
            var openingBalances = await accounts.SystemAsync(
                leg.UserId, LedgerAccountPurpose.OpeningBalances, cancellationToken);

            builder.Add(openingBalances.Id, leg.OpeningBalanceAmount, currency);
        }

        if (leg.IncomeAmount != 0)
        {
            // The leg's category is for the expense when there is one, otherwise for the income.
            var category = await accounts.CategoryAsync(
                leg.UserId, leg.ExpenseAmount == 0 ? leg.CategoryId : null, LedgerAccountType.Income, cancellationToken);

            builder.Add(category.Id, -leg.IncomeAmount, currency);
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
