using Ledger.Modules.Wallets.Domain;

namespace Ledger.Modules.Wallets.Application.GetWallets;

public sealed class GetWalletsHandler(IWalletRepository walletRepository)
{
    private readonly IWalletRepository _walletRepository = walletRepository;
    public async Task<IReadOnlyList<WalletDto>> Handle(
        GetWalletsQuery query, 
        CancellationToken cancellationToken)
    {
        var wallets = await _walletRepository.GetByUserIdAsync(
            query.UserId,
            cancellationToken
        );

        return wallets
            .Select(wallet => new WalletDto(
                wallet.Id,
                wallet.Name,
                wallet.Currency
            )).ToList();
    }
}