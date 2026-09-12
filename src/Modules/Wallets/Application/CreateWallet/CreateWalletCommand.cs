namespace Ledger.Modules.Wallets.Application.CreateWallet;

public sealed record CreateWalletCommand(
    Guid UserId,
    string Name,
    string Currency
);