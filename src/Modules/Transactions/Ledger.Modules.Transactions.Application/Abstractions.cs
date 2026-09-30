using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application;

public interface ITransactionsDbContext
{
    DbSet<Transaction> Transactions { get; }
    DbSet<Posting> Postings { get; }
    DbSet<LedgerAccount> LedgerAccounts { get; }
    DbSet<PendingWalletAssignment> PendingWalletAssignments { get; }
    DbSet<Counterparty> Counterparties { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Runs the work in a database transaction (reusing the ambient one if any).</summary>
    Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken);

    /// <summary>
    /// Loads and row-locks (<c>SELECT … FOR UPDATE</c>) the given accounts until the current
    /// database transaction ends, so concurrent postings cannot overdraw a wallet.
    /// </summary>
    Task<IReadOnlyList<LedgerAccount>> LockAccountsAsync(
        IReadOnlyCollection<AccountId> accountIds,
        CancellationToken cancellationToken);

    /// <summary>Forgets every ledger change tracked but not saved yet (used when an operation is rejected).</summary>
    void DiscardLedgerChanges();

    /// <summary>Loads and row-locks a pending wallet assignment.</summary>
    Task<PendingWalletAssignment?> LockAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken);
}

public interface ITransactionsEventBus : IEventBus;
