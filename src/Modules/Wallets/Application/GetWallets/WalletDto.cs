namespace Ledger.Modules.Wallets.Application.GetWallets;

public sealed record WalletDto(
    Guid Id,
    string Name,
    string Currency);