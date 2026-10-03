namespace Ledger.Modules.Transactions.Domain;

public enum TransactionKind
{
    OpeningBalance,
    Income,
    Expense,
    Transfer,
    Loan,
    Split,
    Settlement,
    WalletAssignment,
    /// <summary>Cancels a previous opening balance that was replaced by a new one.</summary>
    OpeningBalanceReversal,
    /// <summary>Reconciles a wallet with its real balance.</summary>
    BalanceAdjustment,
    /// <summary>A debt that already existed before using Ledger.</summary>
    InitialDebt,
    /// <summary>Cancels another transaction (edit, delete or shared change).</summary>
    Reversal,
    /// <summary>Buys an investment asset with money of a wallet.</summary>
    InvestmentBuy,
    /// <summary>Sells (part of) an investment asset into a wallet.</summary>
    InvestmentSell,
    /// <summary>An investment the user already owned before using Ledger.</summary>
    InvestmentOpening
}
