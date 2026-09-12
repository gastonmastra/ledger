namespace Ledger.Api.Wallets;

public sealed record CreateWalletRequest(
    string Name,
    string Currency
);