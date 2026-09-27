using Ledger.Modules.Accounts.Domain;

namespace Ledger.Modules.Accounts.Application;

public interface IAccountReader
{
    Task<Account?> GetAsync(
        Guid accountId,
        CancellationToken cancellationToken);

    Task<HashSet<Guid>> GetExistingIdsAsync(
        IEnumerable<Guid> accountIds,
        CancellationToken cancellationToken);
}