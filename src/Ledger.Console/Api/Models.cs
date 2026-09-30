namespace Ledger.Console.Api;

// Copies of the API contracts: the console only knows the HTTP API, not the modules.

public sealed record Me(Guid Id, Guid PersonId, string Email, string DisplayName);

public sealed record Wallet(Guid Id, string Name, string Currency, decimal Balance, string Status, DateTimeOffset CreatedAt, bool IsReady);

public sealed record Category(Guid Id, string Name, string Type, bool IsArchived);

public sealed record Contact(Guid Id, Guid PersonId, string Name, string? Email, Guid? LinkedUserId, bool IsLinked, string Origin, bool IsInUse);

public sealed record GroupMember(Guid PersonId, string DisplayName, Guid? UserId, bool IsMe, bool HasActivity);

public sealed record Group(Guid Id, string Name, Guid CreatedBy, DateTimeOffset CreatedAt, IReadOnlyList<GroupMember> Members);

public sealed record Posting(
    Guid AccountId, string AccountName, string AccountPurpose, Guid? WalletId,
    decimal Amount, string Currency, Guid? CounterpartyId, string? CounterpartyName);

public sealed record Transaction(
    Guid Id, DateTimeOffset OccurredAt, string? Description, string Kind, Guid? CorrelationId, IReadOnlyList<Posting> Postings);

public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record CurrencySummary(string Currency, decimal Wallets, decimal OwedToMe, decimal IOwe, decimal Pending, decimal Net);

public sealed record WalletBalance(Guid WalletId, string Name, string Currency, decimal Balance);

public sealed record CounterpartyBalance(Guid CounterpartyId, string Name, string Kind, string Currency, decimal OwedToMe, decimal IOwe, decimal Net);

public sealed record Summary(IReadOnlyList<CurrencySummary> Totals, IReadOnlyList<WalletBalance> Wallets, IReadOnlyList<CounterpartyBalance> Counterparties);

public sealed record PendingAssignment(
    Guid Id, Guid OperationId, decimal Amount, string Currency, Guid? CounterpartyId, string? CounterpartyName,
    string? Description, DateTimeOffset OccurredAt);

public sealed record Operation(Guid Id, string Kind, string Status, string? RejectionReason, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed record Debt(
    Guid Id, Guid OperationId, Guid? GroupId, string? GroupName, Guid CounterpartyPersonId, string CounterpartyName,
    string Direction, decimal Amount, string Currency, string Status, bool IsSettling, string? Description, DateTimeOffset CreatedAt);

public sealed record ContactDebts(Guid PersonId, string Name, string Currency, decimal OwedToMe, decimal IOwe, decimal Net);

public sealed record MyDebts(IReadOnlyList<Debt> Debts, IReadOnlyList<ContactDebts> ByContact);

public sealed record MemberBalance(Guid PersonId, string Name, string Currency, decimal Balance);

public sealed record SuggestedPayment(Guid FromPersonId, string FromName, Guid ToPersonId, string ToName, decimal Amount, string Currency);

public sealed record GroupBalances(
    Guid GroupId, string GroupName, IReadOnlyList<MemberBalance> Balances,
    IReadOnlyList<SuggestedPayment> SuggestedPayments, IReadOnlyList<Debt> Debts);

public sealed record SplitPayer(Guid PersonId, decimal Amount, Guid? WalletId = null);

public sealed record SplitParticipant(Guid PersonId, decimal? Value = null);

public sealed record TransactionFilter(
    int Page = 1, Guid? WalletId = null, DateTimeOffset? From = null, DateTimeOffset? To = null,
    Guid? ContactId = null, Guid? CategoryId = null);
