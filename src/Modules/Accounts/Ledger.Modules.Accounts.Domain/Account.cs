using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Accounts.Domain;

/// <summary>A bucket in the chart of accounts of one user.</summary>
public sealed class Account
{
    /// <summary>Accounts every user gets when registering.</summary>
    public static readonly IReadOnlyDictionary<AccountPurpose, (string Name, AccountType Type)> SystemAccounts =
        new Dictionary<AccountPurpose, (string, AccountType)>
        {
            [AccountPurpose.Receivables] = ("Owed to me", AccountType.Asset),
            [AccountPurpose.Payables] = ("I owe", AccountType.Liability),
            [AccountPurpose.OpeningBalances] = ("Opening balances", AccountType.Equity),
            [AccountPurpose.CurrencyExchange] = ("Currency exchange", AccountType.Equity),
            [AccountPurpose.Uncategorized] = ("Uncategorized", AccountType.Expense),
            [AccountPurpose.UncategorizedIncome] = ("Uncategorized income", AccountType.Income),
            [AccountPurpose.PendingSettlements] = ("Pending settlements", AccountType.Asset)
        };

    public const int NameMaxLength = 200;

    private Account(
        Guid id,
        Guid ownerId,
        string name,
        AccountType type,
        AccountPurpose purpose,
        Currency? currency,
        Guid? referenceId)
    {
        Id = id;
        OwnerId = ownerId;
        Name = ValidateName(name);
        Type = type;
        Purpose = purpose;
        Currency = currency;
        ReferenceId = referenceId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; }
    public AccountType Type { get; private set; }
    public AccountPurpose Purpose { get; private set; }

    /// <summary>Only wallet accounts are bound to a single currency.</summary>
    public Currency? Currency { get; private set; }

    /// <summary>Id of the entity of another module this account represents (e.g. a wallet).</summary>
    public Guid? ReferenceId { get; private set; }

    public bool IsArchived { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsSystem => SystemAccounts.ContainsKey(Purpose);

    public static Account CreateCategory(Guid ownerId, string name, AccountType type)
    {
        if (type is not (AccountType.Income or AccountType.Expense))
            throw new DomainException(
                "account.invalid_category_type",
                "A category must be an Income or an Expense account.");

        return new Account(Guid.NewGuid(), ownerId, name, type, AccountPurpose.Category, null, null);
    }

    public static Account CreateWalletAccount(Guid ownerId, Guid walletId, string name, Currency currency)
        => new(Guid.NewGuid(), ownerId, name, AccountType.Asset, AccountPurpose.Wallet, currency, walletId);

    public static Account CreateSystemAccount(Guid ownerId, AccountPurpose purpose)
    {
        if (!SystemAccounts.TryGetValue(purpose, out var definition))
            throw new DomainException("account.not_system", $"'{purpose}' is not a system account.");

        return new Account(Guid.NewGuid(), ownerId, definition.Name, definition.Type, purpose, null, null);
    }

    public void Rename(string name)
    {
        EnsureNotSystem();
        Name = ValidateName(name);
    }

    public void Archive()
    {
        EnsureNotSystem();
        IsArchived = true;
    }

    private void EnsureNotSystem()
    {
        if (IsSystem)
            throw new DomainException("account.system", "System accounts cannot be changed.");
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("account.name_required", "Account name is required.");

        name = name.Trim();

        if (name.Length > NameMaxLength)
            throw new DomainException(
                "account.name_too_long",
                $"Account name cannot be longer than {NameMaxLength} characters.");

        return name;
    }
}
