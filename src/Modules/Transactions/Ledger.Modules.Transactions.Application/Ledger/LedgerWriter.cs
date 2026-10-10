using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.Modules.Transactions.Contracts;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Ledger;

/// <summary>
/// Persists balanced transactions as one batch: locks the wallet accounts involved, checks the
/// books lock of every owner, enforces the no-overdraft rule on the <b>final</b> balance of each
/// wallet (so a reversal and its replacement are judged together), updates the running balances
/// and reports them to other modules.
/// Must be called inside <see cref="ITransactionsDbContext.InTransactionAsync{T}"/>.
/// </summary>
internal sealed class LedgerWriter(
    ITransactionsDbContext dbContext,
    ITransactionsEventBus eventBus)
{
    public async Task WriteAsync(
        IReadOnlyCollection<Transaction> transactions,
        CancellationToken cancellationToken)
    {
        var accountIds = transactions
            .SelectMany(t => t.Postings)
            .Select(p => p.AccountId)
            .Distinct()
            .ToList();

        var accounts = (await dbContext.LockAccountsAsync(accountIds, cancellationToken))
            .ToDictionary(a => a.Id);

        var missing = accountIds.Where(id => !accounts.ContainsKey(id)).ToList();

        if (missing.Count != 0)
            throw new NotYetAvailableException(
                $"Ledger accounts {string.Join(", ", missing)} are not known yet.");

        await EnsureBooksOpenAsync(transactions, cancellationToken);

        foreach (var transaction in transactions)
        {
            transaction.Validate();

            foreach (var accountId in transaction.Postings.Select(p => p.AccountId).Distinct())
            {
                // Reversals may cancel postings on an archived account; new entries may not.
                if (!transaction.IsReversal)
                    accounts[accountId].EnsureUsableBy(transaction.OwnerId);
            }

            dbContext.Transactions.Add(transaction);
        }

        var walletMovements = transactions
            .SelectMany(t => t.Postings.Select(p => (Transaction: t, Posting: p)))
            .Where(x => accounts[x.Posting.AccountId].IsWallet)
            .GroupBy(x => (x.Posting.AccountId, x.Posting.Currency));

        var touchedWallets = new List<LedgerAccount>();

        foreach (var movement in walletMovements)
        {
            var wallet = accounts[movement.Key.AccountId];
            var count = movement.Select(x => x.Transaction.Id).Distinct().Count();

            wallet.ApplyToWallet(movement.Sum(x => x.Posting.Amount), movement.Key.Currency, count);
            touchedWallets.Add(wallet);
        }

        foreach (var wallet in touchedWallets)
        {
            await eventBus.PublishAsync(
                new WalletBalanceChanged(wallet.ReferenceId!.Value, wallet.Balance, wallet.TransactionCount, wallet.Version),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureBooksOpenAsync(IReadOnlyCollection<Transaction> transactions, CancellationToken cancellationToken)
    {
        var owners = transactions.Select(t => t.OwnerId).Distinct().ToList();

        var locks = await dbContext.BooksLocks
            .AsNoTracking()
            .Where(l => owners.Contains(l.OwnerId) && l.LockedUntil != null)
            .ToDictionaryAsync(l => l.OwnerId, cancellationToken);

        foreach (var transaction in transactions)
        {
            if (locks.TryGetValue(transaction.OwnerId, out var booksLock))
                booksLock.EnsureOpen(transaction.OccurredAt);
        }
    }
}
