namespace Ledger.Api.Transactions;

public sealed record CreateTransactionRequest(
    DateTimeOffset OccurredAt,
    string? Description,
    IReadOnlyCollection<CreateTransactionRequestPosting> Postings);

public sealed record CreateTransactionRequestPosting(
    Guid AccountId,
    decimal Amount);