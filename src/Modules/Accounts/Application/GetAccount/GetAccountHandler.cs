using Ledger.Modules.Accounts.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Accounts.Application.GetAccount;

public sealed class GetAccountHandler(
    AccountsDbContext dbContext
)
{
    private readonly AccountsDbContext _dbContext = dbContext;
    public async Task<AccountDto?> HandleAsync(GetAccountQuery query, CancellationToken cancellationToken)
    {
        var account =  await _dbContext.Accounts
            .SingleOrDefaultAsync(
                x => x.Id == query.AccountId,
                cancellationToken
            );

        if (account is null)
            return null;

        return new AccountDto(
            account.Id,
            account.Name,
            account.Type,
            account.IsArchived);
    }
    
}