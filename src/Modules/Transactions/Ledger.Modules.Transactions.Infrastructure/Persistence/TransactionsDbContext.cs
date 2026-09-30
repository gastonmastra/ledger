using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Transactions.Application;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Infrastructure.Persistence;

public sealed class TransactionsDbContext(DbContextOptions<TransactionsDbContext> options)
    : DbContext(options), ITransactionsDbContext
{
    public const string Schema = "transactions";

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Posting> Postings => Set<Posting>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<PendingWalletAssignment> PendingWalletAssignments => Set<PendingWalletAssignment>();
    public DbSet<Counterparty> Counterparties => Set<Counterparty>();

    public Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
        => PersistenceExtensions.InTransactionAsync(this, work, cancellationToken);

    public async Task<IReadOnlyList<LedgerAccount>> LockAccountsAsync(
        IReadOnlyCollection<AccountId> accountIds,
        CancellationToken cancellationToken)
    {
        var ids = accountIds.Select(x => x.Value).ToArray();

        // Ordered locking avoids deadlocks between concurrent postings.
        return await LedgerAccounts
            .FromSql($"""
                SELECT * FROM transactions.ledger_accounts
                WHERE "Id" = ANY({ids})
                ORDER BY "Id"
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
    }

    public Task<PendingWalletAssignment?> LockAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken)
        => PendingWalletAssignments
            .FromSql($"""
                SELECT * FROM transactions.pending_wallet_assignments
                WHERE "Id" = {assignmentId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

    public void DiscardLedgerChanges()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is Transaction or Posting or LedgerAccount or PendingWalletAssignment)
            .ToList();

        foreach (var entry in entries)
            entry.State = EntityState.Detached;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.UseUtcDateTimeOffsets();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransactionsDbContext).Assembly);
        modelBuilder.AddMessagingTables();
    }
}
