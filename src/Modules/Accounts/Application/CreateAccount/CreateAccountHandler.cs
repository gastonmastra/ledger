using Ledger.Modules.Accounts.Domain;
using Ledger.Modules.Accounts.Infrastructure;

namespace Ledger.Modules.Accounts.Application.CreateAccount;

public sealed class CreateAccountHandler(
    AccountsDbContext dbContext)
{
    public async Task<Guid> Handle(
        CreateAccountCommand command,
        CancellationToken cancellationToken
    )
    {
        var account = Account.Create(
            command.Name,
            command.AccountType);

        dbContext.Accounts.Add(account);

        await dbContext.SaveChangesAsync(cancellationToken);

        return account.Id;
    }
}