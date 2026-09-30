using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.Modules.Transactions.Contracts;
using Ledger.Modules.Transactions.Domain;

namespace Ledger.Modules.Transactions.Application.Ledger;

/// <summary>
/// Persists balanced transactions: locks the wallet accounts involved, enforces the
/// no-overdraft rule, updates the wallet running balances and reports them to other modules.
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

        var touchedWallets = new HashSet<LedgerAccount>();

        foreach (var transaction in transactions)
        {
            transaction.Validate();

            foreach (var accountId in transaction.Postings.Select(p => p.AccountId).Distinct())
                accounts[accountId].EnsureUsableBy(transaction.OwnerId);

            var walletMovements = transaction.Postings
                .Where(p => accounts[p.AccountId].IsWallet)
                .GroupBy(p => (p.AccountId, p.Currency));

            foreach (var movement in walletMovements)
            {
                var wallet = accounts[movement.Key.AccountId];
                wallet.ApplyToWallet(movement.Sum(p => p.Amount), movement.Key.Currency);
                touchedWallets.Add(wallet);
            }

            dbContext.Transactions.Add(transaction);
        }

        foreach (var wallet in touchedWallets)
        {
            await eventBus.PublishAsync(
                new WalletBalanceChanged(wallet.ReferenceId!.Value, wallet.Balance, wallet.TransactionCount, wallet.Version),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
