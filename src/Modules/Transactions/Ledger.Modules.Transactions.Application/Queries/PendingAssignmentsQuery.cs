using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Queries;

/// <param name="Amount">Positive: money I received. Negative: money I paid.</param>
public sealed record PendingAssignmentDto(
    Guid Id,
    Guid OperationId,
    decimal Amount,
    string Currency,
    Guid? CounterpartyId,
    string? CounterpartyName,
    string? Description,
    DateTimeOffset OccurredAt);

public sealed record GetPendingAssignmentsQuery(Guid UserId) : IQuery<IReadOnlyList<PendingAssignmentDto>>;

internal sealed class PendingAssignmentsQueryHandler(ITransactionsDbContext dbContext)
    : IQueryHandler<GetPendingAssignmentsQuery, IReadOnlyList<PendingAssignmentDto>>
{
    public async Task<IReadOnlyList<PendingAssignmentDto>> Handle(
        GetPendingAssignmentsQuery query,
        CancellationToken cancellationToken)
    {
        var names = await dbContext.Counterparties
            .AsNoTracking()
            .Where(c => c.OwnerId == query.UserId)
            .ToDictionaryAsync(c => c.PersonId, c => c.Name, cancellationToken);

        var pending = await dbContext.PendingWalletAssignments
            .AsNoTracking()
            .Where(x => x.OwnerId == query.UserId && x.Status == PendingWalletAssignmentStatus.Pending)
            .OrderBy(x => x.OccurredAt)
            .ToListAsync(cancellationToken);

        return pending
            .Select(x => new PendingAssignmentDto(
                x.Id,
                x.OperationId,
                x.Amount,
                x.Currency.ToString(),
                x.CounterpartyId,
                x.CounterpartyId is { } id ? names.GetValueOrDefault(id) : null,
                x.Description,
                x.OccurredAt))
            .ToList();
    }
}
