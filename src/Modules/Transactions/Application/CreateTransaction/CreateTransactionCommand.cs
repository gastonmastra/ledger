namespace Ledger.Modules.Transactions.Application.CreateTransaction;

public sealed record CreateTransactionCommand(
    DateTimeOffset OccurredAt,
    string? Description,
    IReadOnlyCollection<CreateTransactionPosting> Postings
);

public sealed record CreateTransactionPosting(
    Guid AccountId,
    decimal Amount
);