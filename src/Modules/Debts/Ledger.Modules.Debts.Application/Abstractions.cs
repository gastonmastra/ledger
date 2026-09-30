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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IDebtsEventBus : IEventBus;
