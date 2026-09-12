namespace Ledger.Api.Wallets;

public sealed record CreateWalletResponse(
    Guid Id,
    string Name,
    string Currency
);