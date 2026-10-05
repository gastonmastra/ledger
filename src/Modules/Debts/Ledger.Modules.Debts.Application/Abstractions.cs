using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Debts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application;

public interface IDebtsDbContext
{
    DbSet<DebtOperation> Operations { get; }
    DbSet<Debt> Debts { get; }
    DbSet<DebtContact> Contacts { get; }
    DbSet<DebtUser> Users { get; }
    DbSet<DebtGroupMember> GroupMembers { get; }
    DbSet<DebtWallet> Wallets { get; }
    DbSet<DebtBooksLock> BooksLocks { get; }
    DbSet<OperationChange> Changes { get; }
    DbSet<PersonMerge> PersonMerges { get; }
    DbSet<DebtLinkRequest> LinkRequests { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Moves every reference to <paramref name="previousPersonId"/> to the user <paramref name="userId"/>: debts,
    /// operations (sides, payers, shares), group members and contacts. A contact whose owner already has another
    /// contact for the user is marked as merged instead.
    /// </summary>
    Task ReplacePersonAsync(Guid previousPersonId, Guid userId, CancellationToken cancellationToken);
}

public interface IDebtsEventBus : IEventBus;
