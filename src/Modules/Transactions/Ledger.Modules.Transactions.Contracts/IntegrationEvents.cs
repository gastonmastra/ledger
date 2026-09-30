namespace Ledger.Modules.Transactions.Contracts;

/// <summary>The balance of a wallet changed. <c>Version</c> grows monotonically per wallet.</summary>
public sealed record WalletBalanceChanged(
    Guid WalletId,
    decimal Balance,
    int TransactionCount,
    long Version);

public static class PositionSides
{
    /// <summary>Money the counterparty owes the user (asset).</summary>
    public const string Receivable = nameof(Receivable);
    /// <summary>Money the user owes the counterparty (liability).</summary>
    public const string Payable = nameof(Payable);
}

/// <summary>
/// A change in what a user and a counterparty owe each other.
/// <c>Amount</c> is debit-signed: a receivable grows with a positive amount, a payable grows with a negative amount.
/// </summary>
public sealed record LedgerPosition(
    Guid CounterpartyId,
    string Side,
    decimal Amount);

/// <summary>
/// The effect of a debt operation on the books of one registered user. The amounts must balance:
/// <c>CashAmount + ExpenseAmount − IncomeAmount + Σ Positions.Amount = 0</c>.
/// </summary>
/// <param name="WalletId">Wallet used for the cash movement. Null means the user has to choose it later
/// (the money waits in the pending settlements account).</param>
/// <param name="CategoryId">Expense category for <c>ExpenseAmount</c>; null means uncategorized.</param>
/// <param name="CashAmount">Money into (+) or out of (−) the user's pocket.</param>
/// <param name="ExpenseAmount">The user's own consumption (split share), debited to an expense category.</param>
/// <param name="IncomeAmount">Income for the user (e.g. a debt somebody already owed them), credited to an income category.</param>
public sealed record LedgerLeg(
    Guid UserId,
    Guid? WalletId,
    Guid? CategoryId,
    decimal CashAmount,
    decimal ExpenseAmount,
    IReadOnlyList<LedgerPosition> Positions,
    decimal IncomeAmount = 0);

/// <summary>
/// Asks the ledger to record a debt operation (loan, split or settlement) for every registered
/// user involved, atomically. Answered with <see cref="LedgerPostingCompleted"/> or
/// <see cref="LedgerPostingRejected"/>.
/// </summary>
public sealed record LedgerPostingRequested(
    Guid OperationId,
    string Kind,
    DateTimeOffset OccurredAt,
    string? Description,
    string Currency,
    Guid InitiatorUserId,
    IReadOnlyList<LedgerLeg> Legs);

public static class LedgerPostingKinds
{
    public const string Loan = nameof(Loan);
    public const string Split = nameof(Split);
    public const string Settlement = nameof(Settlement);
    public const string InitialDebt = nameof(InitialDebt);
}

public sealed record LedgerPostingCompleted(Guid OperationId);

public sealed record LedgerPostingRejected(Guid OperationId, string Reason);
