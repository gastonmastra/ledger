using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Wallets.Domain;

public enum WalletStatus
{
    Active,
    Archived
}

/// <summary>
/// A place where the user keeps money (bank account, cash, card...). The money itself is
/// recorded in the ledger; the wallet keeps a projection of its balance for display and rules.
/// </summary>
public sealed class Wallet
{
    public const int NameMaxLength = 100;

    private Wallet(Guid id, Guid userId, string name, Currency currency)
    {
        Id = id;
        UserId = userId;
        Name = name;
        Currency = currency;
        Status = WalletStatus.Active;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; }
    public Currency Currency { get; private set; }
    public WalletStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Balance as last reported by the ledger.</summary>
    public decimal Balance { get; private set; }

    /// <summary>Number of ledger transactions that touched the wallet, as last reported by the ledger.</summary>
    public int TransactionCount { get; private set; }

    public long BalanceVersion { get; private set; }

    /// <summary>The ledger confirmed the wallet account exists, so it can be used in transactions.</summary>
    public bool IsReady => BalanceVersion > 0;

    public static Wallet Create(Guid userId, string name, Currency currency, decimal openingBalance)
    {
        if (openingBalance < 0)
            throw new DomainException("wallet.negative_opening_balance", "The opening balance cannot be negative.");

        Money.EnsureCents(openingBalance, "opening balance");

        return new Wallet(Guid.NewGuid(), userId, ValidateName(name), currency)
        {
            // Optimistic projection until the ledger confirms the opening balance.
            Balance = openingBalance,
            TransactionCount = openingBalance > 0 ? 1 : 0
        };
    }

    public void Rename(string name)
    {
        EnsureActive();
        Name = ValidateName(name);
    }

    public void Archive()
    {
        EnsureActive();

        if (Balance != 0)
            throw new DomainException(
                "wallet.not_empty",
                FormattableString.Invariant(
                    $"Only wallets with a zero balance can be archived (current balance: {Balance:0.00} {Currency})."));

        Status = WalletStatus.Archived;
    }

    public void EnsureCanBeDeleted()
    {
        if (TransactionCount > 0)
            throw new DomainException(
                "wallet.has_transactions",
                "Only wallets without transactions can be deleted. Archive it instead.");
    }

    /// <summary>Applies a balance reported by the ledger, ignoring stale (out of order) reports.</summary>
    public void ApplyLedgerBalance(decimal balance, int transactionCount, long version)
    {
        if (version <= BalanceVersion)
            return;

        Balance = balance;
        TransactionCount = transactionCount;
        BalanceVersion = version;
    }

    private void EnsureActive()
    {
        if (Status == WalletStatus.Archived)
            throw new DomainException("wallet.archived", "The wallet is archived.");
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("wallet.name_required", "Wallet name is required.");

        name = name.Trim();

        if (name.Length > NameMaxLength)
            throw new DomainException(
                "wallet.name_too_long",
                $"Wallet name cannot be longer than {NameMaxLength} characters.");

        return name;
    }
}
