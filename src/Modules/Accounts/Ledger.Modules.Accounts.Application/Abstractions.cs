using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Accounts.Contracts;
using Ledger.Modules.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Accounts.Application;

public interface IAccountsDbContext
{
    DbSet<Account> Accounts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAccountsEventBus : IEventBus;

internal static class AccountEvents
{
    public static AccountCreated Created(Account account, decimal openingBalance = 0, DateTimeOffset? openingBalanceAt = null)
        => new(
            account.Id,
            account.OwnerId,
            account.Name,
            account.Type.ToString(),
            account.Purpose.ToString(),
            account.Currency?.ToString(),
            account.ReferenceId,
            openingBalance,
            account.CreatedAt,
            openingBalanceAt);
}
