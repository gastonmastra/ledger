namespace Ledger.Modules.Accounts.Contracts;

public static class AccountTypes
{
    public const string Asset = nameof(Asset);
    public const string Liability = nameof(Liability);
    public const string Equity = nameof(Equity);
    public const string Income = nameof(Income);
    public const string Expense = nameof(Expense);
}

public static class AccountPurposes
{
    /// <summary>Asset account backing a wallet (ReferenceId = wallet id).</summary>
    public const string Wallet = nameof(Wallet);
    /// <summary>User-created income or expense category.</summary>
    public const string Category = nameof(Category);
    /// <summary>Money other people owe to the user, per counterparty.</summary>
    public const string Receivables = nameof(Receivables);
    /// <summary>Money the user owes to other people, per counterparty.</summary>
    public const string Payables = nameof(Payables);
    public const string OpeningBalances = nameof(OpeningBalances);
    public const string CurrencyExchange = nameof(CurrencyExchange);
    /// <summary>Expense account used when no category is chosen.</summary>
    public const string Uncategorized = nameof(Uncategorized);
    /// <summary>Income account used when no category is chosen.</summary>
    public const string UncategorizedIncome = nameof(UncategorizedIncome);
    /// <summary>Money paid/received on the user's behalf that waits for a wallet to be chosen.</summary>
    public const string PendingSettlements = nameof(PendingSettlements);
    /// <summary>Differences recorded when the user reconciles a wallet with its real balance.</summary>
    public const string BalanceAdjustments = nameof(BalanceAdjustments);
    /// <summary>Asset account holding the cost of an investment asset (ReferenceId = asset id).</summary>
    public const string Investment = nameof(Investment);
    /// <summary>Realized gains and losses of investment sales (neither income nor expenses).</summary>
    public const string InvestmentGains = nameof(InvestmentGains);
}

public sealed record AccountCreated(
    Guid AccountId,
    Guid OwnerId,
    string Name,
    string Type,
    string Purpose,
    string? Currency,
    Guid? ReferenceId,
    decimal OpeningBalance,
    DateTimeOffset CreatedAt,
    DateTimeOffset? OpeningBalanceAt = null);

public sealed record AccountRenamed(Guid AccountId, Guid OwnerId, string Name);

public sealed record AccountArchived(Guid AccountId, Guid OwnerId);

public sealed record AccountDeleted(Guid AccountId, Guid OwnerId);
