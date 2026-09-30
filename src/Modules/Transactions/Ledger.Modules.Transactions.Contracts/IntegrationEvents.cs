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
/// <c>CashAmount + ExpenseAmount − IncomeAmount + OpeningBalanceAmount + Σ Positions.Amount = 0</c>.
/// </summary>
/// <param name="WalletId">Wallet used for the cash movement. Null means the user has to choose it later
/// (the money waits in the pending settlements account).</param>
/// <param name="CategoryId">Expense category for <c>ExpenseAmount</c>; null means uncategorized.</param>
/// <param name="CashAmount">Money into (+) or out of (−) the user's pocket.</param>
/// <param name="ExpenseAmount">The user's own consumption (split share), debited to an expense category.</param>
/// <param name="IncomeAmount">Income for the user, credited to an income category.</param>
/// <param name="OpeningBalanceAmount">Debit-signed amount on "Opening balances" (e.g. a debt that existed
/// before using Ledger: it changes net worth but is neither income nor expense).</param>
public sealed record LedgerLeg(
    Guid UserId,
    Guid? WalletId,
    Guid? CategoryId,
    decimal CashAmount,
    decimal ExpenseAmount,
    IReadOnlyList<LedgerPosition> Positions,
    decimal IncomeAmount = 0,
    decimal OpeningBalanceAmount = 0);

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

/// <summary>
/// A shared operation changed (edit, delete, restore). For every user in <c>UserIds</c> the ledger
/// reverses the current transactions of any <c>PreviousOperationIds</c> (every earlier version, so users
/// who kept an older version after a dispute are also brought up to date; open pending wallet
/// assignments are cancelled) and records their leg of the new version, if any (none for a delete), as the next
/// revision. All users are updated atomically. Answered with <see cref="LedgerRevisionCompleted"/>
/// or <see cref="LedgerRevisionRejected"/>.
/// </summary>
/// <param name="ChangeId">Id of the change in the Debts module (used to correlate the answer).</param>
/// <param name="Action">Edit, Delete or Restore.</param>
/// <param name="NewOperationId">Operation id of the new version (correlation of the new transactions); null for a delete.</param>
public sealed record LedgerRevisionRequested(
    Guid ChangeId,
    string Action,
    IReadOnlyList<Guid> PreviousOperationIds,
    Guid? NewOperationId,
    string Kind,
    DateTimeOffset OccurredAt,
    string? Description,
    string Currency,
    Guid ChangedByUserId,
    string Reason,
    IReadOnlyList<Guid> UserIds,
    IReadOnlyList<LedgerLeg> Legs);

public sealed record LedgerRevisionCompleted(Guid ChangeId);

public sealed record LedgerRevisionRejected(Guid ChangeId, string Reason);

/// <summary>A user moved the date up to which their books are closed (null: nothing is locked).</summary>
public sealed record BooksLockChanged(Guid UserId, DateTimeOffset? LockedUntil);

public sealed record LedgerPostingRejected(Guid OperationId, string Reason);
