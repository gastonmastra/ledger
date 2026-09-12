using Ledger.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Identity.Infrastructure.Persistence;

public sealed class UserRepository(IdentityDbContext dbContext) : IUserRepository
{
    private readonly IdentityDbContext _dbContext = dbContext;
    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        await _dbContext.AddAsync(
            user,
            cancellationToken);
    }

    public async Task<User?> GetByExternalIdAsync(
        string externalId, 
        CancellationToken cancellationToken)
    {
        return await _dbContext
            .Users
            .SingleOrDefaultAsync(
                x => x.ExternalId == externalId, 
                cancellationToken);    
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _dbContext.SaveChangesAsync(
            cancellationToken);
    }
}