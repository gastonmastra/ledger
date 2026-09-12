using Ledger.Modules.Wallets.Application.GetWallets;

namespace Ledger.Modules.Wallets.Domain;

public interface IWalletRepository
{
    Task AddAsync(
        Wallet wallet,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WalletDto>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken);
}