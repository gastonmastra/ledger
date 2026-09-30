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
    WalletAssignment
}
