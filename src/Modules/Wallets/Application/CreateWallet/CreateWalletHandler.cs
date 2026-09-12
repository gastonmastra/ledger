using Ledger.Modules.Wallets.Domain;

namespace Ledger.Modules.Wallets.Application.CreateWallet;

public sealed class CreateWalletHandler(IWalletRepository walletRepository)
{
    private readonly IWalletRepository _walletRepository = walletRepository;

    public async Task<CreateWalletResult> Handle(
        CreateWalletCommand command,
        CancellationToken cancellationToken)
    {
        var wallet = Wallet.Create(
            command.UserId,
            command.Name,
            command.Currency
        );

        await _walletRepository.AddAsync(
            wallet,
            cancellationToken
        );

        return new CreateWalletResult(
            wallet.Id,
            wallet.Name,
            wallet.Currency
        );
    }
}