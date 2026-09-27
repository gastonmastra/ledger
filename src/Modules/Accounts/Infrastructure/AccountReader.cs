using Ledger.Modules.Accounts.Application;
using Ledger.Modules.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Accounts.Infrastructure;

public sealed class AccountReader(
    AccountsDbContext dbContext
) : IAccountReader
{
    public Task<Account?> GetAsync(
        Guid accountId, 
        CancellationToken cancellationToken)
    {
        return dbContext.Accounts
            .SingleOrDefaultAsync(
                x => x.Id == accountId,
                cancellationToken
            );
    }

    public async Task<HashSet<Guid>> GetExistingIdsAsync(
        IEnumerable<Guid> accountIds, 
        CancellationToken cancellationToken)
    {
        var ids = accountIds.ToHashSet();

        return await dbContext.Accounts
            .Where(x => ids.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken);
    }
}