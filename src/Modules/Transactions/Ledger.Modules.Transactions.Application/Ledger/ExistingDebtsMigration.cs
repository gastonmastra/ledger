using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Ledger;

/// <summary>
/// Rule change: debts that existed before using Ledger are no longer income or expenses. Every
/// current "existing debt" transaction posted against an income or expense account is reversed and
/// replaced by one against "Opening balances" (a system change, visible in its history). Idempotent.
/// </summary>
public sealed record MigrateExistingDebtsCommand : ICommand<int>;

internal sealed class MigrateExistingDebtsHandler(
    ITransactionsDbContext dbContext,
    AccountResolver accounts,
    LedgerWriter writer) : ICommandHandler<MigrateExistingDebtsCommand, int>
{
    public const string Reason = "Existing debts no longer count as income or expenses";

    public async Task<int> Handle(MigrateExistingDebtsCommand command, CancellationToken cancellationToken)
    {
        var incomeOrExpense = dbContext.LedgerAccounts
            .Where(a => a.Type == LedgerAccountType.Income || a.Type == LedgerAccountType.Expense)
            .Select(a => a.Id);

        var ids = await dbContext.Transactions
            .Where(t => t.Kind == TransactionKind.InitialDebt
                        && t.ReversedById == null
                        && t.Postings.Any(p => incomeOrExpense.Contains(p.AccountId)))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            await dbContext.InTransactionAsync(async () =>
            {
                var original = await dbContext.Transactions
                    .Include(t => t.Postings)
                    .SingleAsync(t => t.Id == id, cancellationToken);
                var openingBalances = await accounts.SystemAsync(
                    original.OwnerId, LedgerAccountPurpose.OpeningBalances, cancellationToken);
                var migratedAccounts = await incomeOrExpense.ToListAsync(cancellationToken);

                var replacement = TransactionBuilder.Create(
                    original.OwnerId, original.OccurredAt, original.Kind, original.Description, original.CorrelationId);

                foreach (var posting in original.Postings)
                {
                    var accountId = migratedAccounts.Contains(posting.AccountId) ? openingBalances.Id : posting.AccountId;
                    replacement.Add(accountId, posting.Amount, posting.Currency, posting.CounterpartyId);
                }

                var transaction = replacement.Build();
                transaction.BecomeRevisionOf(original, original.Revision + 1, null, ChangeAction.System, Reason);
                var reversal = Transaction.ReversalOf(original, null, ChangeAction.System, Reason);

                await writer.WriteAsync([reversal, transaction], cancellationToken);
                return true;
            }, cancellationToken);
        }

        return ids.Count;
    }
}
