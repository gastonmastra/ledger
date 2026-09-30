using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Transactions.Domain;

public enum LedgerAccountPurpose
{
    Wallet,
    Category,
    Receivables,
    Payables,
    OpeningBalances,
    CurrencyExchange,
    Uncategorized,
    UncategorizedIncome,
    PendingSettlements
}

public enum LedgerAccountType
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense
}

/// <summary>
/// Local copy of an account of the Accounts module. For wallet accounts it also keeps the running
/// balance, which is what enforces the "no overdraft" rule.
/// </summary>
public sealed class LedgerAccount
{
    private LedgerAccount()
    {
        Name = null!;
    }

    public AccountId Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; }
    public LedgerAccountType Type { get; private set; }
    public LedgerAccountPurpose Purpose { get; private set; }
    public Currency? Currency { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public bool IsArchived { get; private set; }

    /// <summary>Running balance. Only maintained for wallet accounts.</summary>
    public decimal Balance { get; private set; }

    public int TransactionCount { get; private set; }

    /// <summary>Grows with every change of the balance; lets consumers discard stale updates.</summary>
    public long Version { get; private set; }

    public bool IsWallet => Purpose == LedgerAccountPurpose.Wallet;

    public static LedgerAccount Create(
        AccountId id,
        Guid ownerId,
        string name,
        LedgerAccountType type,
        LedgerAccountPurpose purpose,
        Currency? currency,
        Guid? referenceId)
        => new()
        {
            Id = id,
            OwnerId = ownerId,
            Name = name,
            Type = type,
            Purpose = purpose,
            Currency = currency,
            ReferenceId = referenceId,
            // Version 1 announces that the wallet can be used in the ledger.
            Version = purpose == LedgerAccountPurpose.Wallet ? 1 : 0
        };

    public void Rename(string name) => Name = name;

    public void Archive() => IsArchived = true;

    public void EnsureUsableBy(Guid ownerId)
    {
        if (OwnerId != ownerId)
            throw new DomainException("ledger.account_not_owned", $"Account '{Name}' does not belong to the user.");

        if (IsArchived)
            throw new DomainException("ledger.account_archived", $"Account '{Name}' is archived.");
    }

    /// <summary>Applies the postings of one transaction that touch this wallet account.</summary>
    public void ApplyToWallet(decimal amount, Currency currency)
    {
        if (!IsWallet)
            throw new InvalidOperationException("Only wallet balances are tracked.");

        if (currency != Currency)
            throw new DomainException(
                "ledger.currency_mismatch",
                $"Wallet '{Name}' holds {Currency}, it cannot receive {currency}.");

        var newBalance = Balance + amount;

        if (newBalance < 0)
            throw new DomainException(
                "ledger.insufficient_funds",
                FormattableString.Invariant(
                    $"Wallet '{Name}' does not have enough money (balance {Balance:0.00} {Currency}, needed {-amount:0.00} {Currency})."));

        Balance = newBalance;
        TransactionCount++;
        Version++;
    }
}
