namespace Ledger.Modules.Wallets.Application.CreateWallet;

public sealed record CreateWalletResult(
    Guid Id,
    string Name,
    string Currency
);