using Ledger.Modules.Wallets.Application.GetWallets;
using Ledger.Modules.Wallets.Domain;
using Ledger.Modules.Wallets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Wallets.Infrastructure;

public sealed class WalletRepository(
    WalletsDbContext dbContext
) : IWalletRepository
{
    public readonly WalletsDbContext _dbContext = dbContext;
    public async Task AddAsync(Wallet wallet, CancellationToken cancellationToken)
    {
        await _dbContext.Wallets.AddAsync(
            wallet,
            cancellationToken
        );

        await _dbContext.SaveChangesAsync(
            cancellationToken
        );
    }

    public async Task<IReadOnlyList<WalletDto>> GetByUserIdAsync(
        Guid userId, 
        CancellationToken cancellationToken)
    {
        return await _dbContext.Wallets
            .Where(x => x.UserId == userId)
            .Select(w => new WalletDto(
                w.Id,
                w.Name,
                w.Currency))
            .ToListAsync(cancellationToken);
    }
}