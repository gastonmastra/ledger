namespace Ledger.Modules.Accounts.Domain;

public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense
}

public enum AccountPurpose
{
    Wallet,
    Category,
    Receivables,
    Payables,
    OpeningBalances,
    CurrencyExchange,
    Uncategorized,
    UncategorizedIncome,
    PendingSettlements,
    BalanceAdjustments,
    Investment,
    InvestmentGains
}
