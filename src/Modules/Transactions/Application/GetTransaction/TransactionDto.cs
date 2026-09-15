namespace Ledger.Modules.Transactions.Application.GetTransaction;


public sealed record TransactionDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string? Description,
    IReadOnlyCollection<PostingDto> Postings);

public sealed record PostingDto(
    Guid AccountId,
    decimal Amount);