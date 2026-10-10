namespace Ledger.Console.Api;

// Copies of the API contracts: the console only knows the HTTP API, not the modules.

public sealed record Me(Guid Id, Guid PersonId, string Email, string DisplayName);

public sealed record Wallet(Guid Id, string Name, string Currency, decimal Balance, string Status, DateTimeOffset CreatedAt, bool IsReady);

public sealed record Category(Guid Id, string Name, string Type, bool IsArchived);

/// <param name="PendingLinkRequestId">A link request waiting for the user's answer (contacts already in use).</param>
public sealed record Contact(
    Guid Id, Guid PersonId, string Name, string? Email, Guid? LinkedUserId, bool IsLinked, string Origin, bool IsInUse,
    Guid? PendingLinkRequestId = null);

/// <param name="Direction">Incoming (I have to answer) or Outgoing (I asked).</param>
/// <param name="Status">Pending, Applying, Applied, Rejected, Cancelled or Failed.</param>
public sealed record LinkRequest(
    Guid Id, string Direction, Guid ContactId, string ContactName, Guid OwnerId, string OwnerName, Guid TargetUserId,
    string TargetName, string Status, string? FailureReason, DateTimeOffset CreatedAt, DateTimeOffset? AnsweredAt);

/// <param name="Amount">Positive: owed to the user; negative: the user owes it.</param>
public sealed record LinkPreviewBalance(Guid CounterpartyId, string Name, string Kind, string Currency, decimal Amount);

public sealed record LinkPreview(Guid RequestId, int Operations, IReadOnlyList<LinkPreviewBalance> Balances);

public sealed record GroupMember(Guid PersonId, string DisplayName, Guid? UserId, bool IsMe, bool HasActivity);

public sealed record Group(Guid Id, string Name, Guid CreatedBy, DateTimeOffset CreatedAt, IReadOnlyList<GroupMember> Members);

public sealed record Posting(
    Guid AccountId, string AccountName, string AccountPurpose, Guid? WalletId,
    decimal Amount, string Currency, Guid? CounterpartyId, string? CounterpartyName, Guid? CategoryId = null);

/// <param name="Status">Current, Changed, Deleted or Reversal.</param>
public sealed record Transaction(
    Guid Id, Guid EntryId, int Revision, DateTimeOffset OccurredAt, string? Description, string Kind, string Status,
    Guid? CorrelationId, string? ChangeReason, bool Editable, bool Deletable, IReadOnlyList<Posting> Postings);

public sealed record TransactionHistoryItem(
    Guid Id, int Revision, string Kind, string Status, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt,
    string? Description, string? Action, string? Reason, Guid? ChangedByUserId, bool ChangedByMe, string? ChangedByName,
    IReadOnlyList<Posting> Postings);

public sealed record TransactionActivity(
    Guid TransactionId, Guid EntryId, string Kind, string Action, string? Description, string Reason,
    DateTimeOffset At, bool ChangedByMe, string? ChangedByName);

public sealed record BooksLock(DateTimeOffset? LockedUntil);

public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record CurrencySummary(
    string Currency, decimal Wallets, decimal OwedToMe, decimal IOwe, decimal Pending, decimal Net, decimal Adjustments);

public sealed record WalletBalance(Guid WalletId, string Name, string Currency, decimal Balance);

public sealed record CounterpartyBalance(Guid CounterpartyId, string Name, string Kind, string Currency, decimal OwedToMe, decimal IOwe, decimal Net);

public sealed record Summary(IReadOnlyList<CurrencySummary> Totals, IReadOnlyList<WalletBalance> Wallets, IReadOnlyList<CounterpartyBalance> Counterparties);

public sealed record PendingAssignment(
    Guid Id, Guid OperationId, decimal Amount, string Currency, Guid? CounterpartyId, string? CounterpartyName,
    string? Description, DateTimeOffset OccurredAt);

public sealed record Operation(Guid Id, string Kind, string Status, string? RejectionReason, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed record Debt(
    Guid Id, Guid OperationId, Guid? GroupId, string? GroupName, Guid CounterpartyPersonId, string CounterpartyName,
    string Direction, decimal Amount, string Currency, string Status, bool IsSettling, string? Description, DateTimeOffset CreatedAt,
    bool PendingChange = false, bool Disputed = false);

public sealed record ContactDebts(Guid PersonId, string Name, string Currency, decimal OwedToMe, decimal IOwe, decimal Net);

public sealed record MyDebts(IReadOnlyList<Debt> Debts, IReadOnlyList<ContactDebts> ByContact);

public sealed record MemberBalance(Guid PersonId, string Name, string Currency, decimal Balance);

public sealed record SuggestedPayment(Guid FromPersonId, string FromName, Guid ToPersonId, string ToName, decimal Amount, string Currency);

public sealed record GroupBalances(
    Guid GroupId, string GroupName, IReadOnlyList<MemberBalance> Balances,
    IReadOnlyList<SuggestedPayment> SuggestedPayments, IReadOnlyList<Debt> Debts);

public sealed record OpeningBalance(decimal Amount, DateTimeOffset? OccurredAt);

public sealed record ReportBucket(DateTimeOffset Start, decimal Income, decimal Expenses, decimal NetWorth);

public sealed record CategoryAmount(Guid AccountId, string Name, string Purpose, decimal Amount);

public sealed record ReportWallet(Guid WalletId, string Name, decimal Balance);

public sealed record Report(
    string Currency, IReadOnlyList<string> Currencies, string Granularity, decimal TotalIncome, decimal TotalExpenses,
    IReadOnlyList<ReportBucket> Buckets, IReadOnlyList<CategoryAmount> SpendingByCategory,
    IReadOnlyList<ReportWallet> WalletBalances);

public sealed record SplitPayer(Guid PersonId, decimal Amount, Guid? WalletId = null);

public sealed record SplitParticipant(Guid PersonId, decimal? Value = null);

public sealed record TransactionFilter(
    int Page = 1, Guid? WalletId = null, DateTimeOffset? From = null, DateTimeOffset? To = null,
    Guid? ContactId = null, Guid? CategoryId = null, bool IncludeHistory = false);

public sealed record PersonAmount(Guid PersonId, string Name, decimal Amount, Guid? WalletId = null);

public sealed record PendingChange(
    Guid ChangeId, string Kind, string RequestedByName, bool RequestedByMe, string Reason,
    IReadOnlyList<string> WaitingFor, bool WaitingForMe);

/// <summary>A loan, existing debt, split or settlement from my point of view (ContactId/WalletId/CategoryId only if I recorded it).</summary>
public sealed record OperationDetails(
    Guid Id, Guid EntryId, int Revision, string Kind, string Status, string? RejectionReason,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, DateTimeOffset OccurredAt, string? Description,
    string Currency, decimal Total, Guid? GroupId, string? GroupName, string? Method, string? Scope,
    bool RecordedByMe, string RecordedByName, Guid? FromPersonId, string? FromName, Guid? ToPersonId, string? ToName,
    Guid? CounterpartyPersonId, string? CounterpartyName, Guid? ContactId, string? Direction, Guid? WalletId,
    Guid? CategoryId, IReadOnlyList<PersonAmount> Payers, IReadOnlyList<PersonAmount> Shares, bool IsDisputed,
    PendingChange? PendingChange, bool CanEdit, bool CanDelete, bool CanRestore, string? BlockedReason,
    IReadOnlyList<Guid> SettlementOperationIds);

public sealed record ChangeSnapshot(
    decimal Total, string Currency, DateTimeOffset OccurredAt, string? Description, string? FromName, string? ToName,
    IReadOnlyList<PersonAmount> Payers, IReadOnlyList<PersonAmount> Shares);

public sealed record Approval(Guid UserId, string Name, string Status, string? Reason, DateTimeOffset? RespondedAt);

public sealed record OperationChange(
    Guid Id, Guid OperationId, Guid EntryId, string OperationKind, string Kind, string Status, string RequestedByName,
    bool RequestedByMe, string Reason, DateTimeOffset CreatedAt, DateTimeOffset? AppliedAt, string? FailureReason,
    Guid? NewOperationId, IReadOnlyList<Approval> Approvals, string? MyApproval, bool Disputed,
    ChangeSnapshot Current, ChangeSnapshot? Proposed);

public sealed record OperationHistory(IReadOnlyList<OperationDetails> Versions, IReadOnlyList<OperationChange> Changes);

/// <param name="Status">Pending (waiting for linked users), Applying, Applied or Failed.</param>
public sealed record ChangeResult(
    Guid ChangeId, Guid OperationId, string Kind, string Status, Guid? NewOperationId, IReadOnlyList<string> WaitingFor);

// Investments

/// <param name="ValuationPrice">Latest sell price, else latest buy price, else average cost (<c>ValuationSource</c>).</param>
public sealed record Asset(
    Guid Id, string Name, string? Symbol, string Type, string Currency, string Status, decimal Quantity, decimal CostBasis,
    decimal? AverageCost, decimal? BuyPrice, decimal? SellPrice, decimal? ValuationPrice, string? ValuationSource,
    decimal MarketValue, decimal UnrealizedGain, decimal RealizedGain, DateTimeOffset? LastTradeAt, bool HasPendingTrade);

/// <param name="Status">Pending, Completed or Rejected.</param>
public sealed record Trade(
    Guid Id, Guid AssetId, string AssetName, string? AssetSymbol, string Kind, decimal Quantity, decimal UnitPrice, decimal Total,
    string Currency, Guid? WalletId, string? WalletCurrency, decimal? WalletAmount, decimal CostRemoved, decimal RealizedGain,
    decimal? CounterPrice, DateTimeOffset OccurredAt, string? Description, string Status, string? RejectionReason);

public sealed record AssetPrice(Guid Id, string Side, decimal Price, DateTimeOffset OccurredAt, string Source, Guid? TradeId);

public sealed record InvestmentsTotal(string Currency, decimal Value, decimal Cost, decimal UnrealizedGain, decimal RealizedGain);

public sealed record InvestmentsBucket(DateTimeOffset Start, decimal Value, decimal Cost);

public sealed record InvestmentsReport(
    string Currency, IReadOnlyList<string> Currencies, string Granularity, IReadOnlyList<InvestmentsBucket> Buckets, decimal RealizedGain);
