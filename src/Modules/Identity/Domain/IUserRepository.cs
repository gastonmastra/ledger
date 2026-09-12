namespace Ledger.Modules.Identity.Domain;

public interface IUserRepository
{
    Task<User?> GetByExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken);

    Task AddAsync(
        User user,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(
        CancellationToken cancellationToken);
}