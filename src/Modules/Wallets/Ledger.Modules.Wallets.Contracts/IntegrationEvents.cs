namespace Ledger.Modules.Wallets.Contracts;

public sealed record WalletCreated(
    Guid WalletId,
    Guid UserId,
    string Name,
    string Currency,
    decimal OpeningBalance,
    DateTimeOffset CreatedAt);

public sealed record WalletRenamed(Guid WalletId, Guid UserId, string Name);

public sealed record WalletArchived(Guid WalletId, Guid UserId);

public sealed record WalletDeleted(Guid WalletId, Guid UserId);
