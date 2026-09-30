using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Queries;

public sealed record PostingDto(
    Guid AccountId,
    string AccountName,
    string AccountPurpose,
    Guid? WalletId,
    decimal Amount,
    string Currency,
    Guid? CounterpartyId,
    string? CounterpartyName);

public sealed record TransactionDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string? Description,
    TransactionKind Kind,
    Guid? CorrelationId,
    IReadOnlyList<PostingDto> Postings);

public sealed record GetTransactionsQuery(
    Guid UserId,
    int Page = 1,
    int PageSize = 20,
    Guid? WalletId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? ContactId = null,
    Guid? CategoryId = null) : IQuery<PagedResult<TransactionDto>>;

public sealed record GetTransactionQuery(Guid UserId, Guid TransactionId) : IQuery<TransactionDto>;

internal sealed class TransactionQueryHandlers(ITransactionsDbContext dbContext)
    : IQueryHandler<GetTransactionsQuery, PagedResult<TransactionDto>>,
      IQueryHandler<GetTransactionQuery, TransactionDto>
{
    private const int MaxPageSize = 100;

    public async Task<PagedResult<TransactionDto>> Handle(GetTransactionsQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var transactions = dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.OwnerId == query.UserId);

        if (query.From is { } from)
            transactions = transactions.Where(t => t.OccurredAt >= from);

        if (query.To is { } to)
            transactions = transactions.Where(t => t.OccurredAt <= to);

        if (query.WalletId is { } walletId)
        {
            var walletAccountIds = dbContext.LedgerAccounts
                .Where(a => a.OwnerId == query.UserId
                            && a.Purpose == LedgerAccountPurpose.Wallet
                            && a.ReferenceId == walletId)
                .Select(a => a.Id);

            transactions = transactions.Where(t => t.Postings.Any(p => walletAccountIds.Contains(p.AccountId)));
        }

        if (query.CategoryId is { } categoryId)
        {
            var categoryAccountId = new AccountId(categoryId);
            transactions = transactions.Where(t => t.Postings.Any(p => p.AccountId == categoryAccountId));
        }

        if (query.ContactId is { } contactId)
        {
            var personIds = dbContext.Counterparties
                .Where(c => c.OwnerId == query.UserId
                            && c.Kind == CounterpartyKind.Contact
                            && c.SourceId == contactId)
                .Select(c => (Guid?)c.PersonId);

            transactions = transactions.Where(t => t.Postings.Any(p => personIds.Contains(p.CounterpartyId)));
        }

        var total = await transactions.CountAsync(cancellationToken);

        var items = await transactions
            .OrderByDescending(t => t.OccurredAt)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(t => t.Postings)
            .ToListAsync(cancellationToken);

        return new PagedResult<TransactionDto>(
            await ToDtosAsync(query.UserId, items, cancellationToken),
            page,
            pageSize,
            total);
    }

    public async Task<TransactionDto> Handle(GetTransactionQuery query, CancellationToken cancellationToken)
    {
        var id = new TransactionId(query.TransactionId);

        var transaction = await dbContext.Transactions
            .AsNoTracking()
            .Include(t => t.Postings)
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (transaction is null || transaction.OwnerId != query.UserId)
            throw new NotFoundException("Transaction", query.TransactionId);

        return (await ToDtosAsync(query.UserId, [transaction], cancellationToken))[0];
    }

    private async Task<IReadOnlyList<TransactionDto>> ToDtosAsync(
        Guid userId,
        IReadOnlyList<Transaction> transactions,
        CancellationToken cancellationToken)
    {
        var accountIds = transactions.SelectMany(t => t.Postings).Select(p => p.AccountId).Distinct().ToList();

        var accounts = await dbContext.LedgerAccounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var counterparties = await dbContext.Counterparties
            .AsNoTracking()
            .Where(c => c.OwnerId == userId)
            .ToDictionaryAsync(c => c.PersonId, c => c.Name, cancellationToken);

        return transactions
            .Select(t => new TransactionDto(
                t.Id.Value,
                t.OccurredAt,
                t.Description,
                t.Kind,
                t.CorrelationId,
                t.Postings
                    .OrderByDescending(p => p.Amount)
                    .Select(p =>
                    {
                        accounts.TryGetValue(p.AccountId, out var account);
                        string? counterpartyName = null;

                        if (p.CounterpartyId is { } cp)
                            counterpartyName = counterparties.GetValueOrDefault(cp);

                        return new PostingDto(
                            p.AccountId.Value,
                            account?.Name ?? "(deleted account)",
                            account?.Purpose.ToString() ?? "",
                            account?.IsWallet == true ? account.ReferenceId : null,
                            p.Amount,
                            p.Currency.ToString(),
                            p.CounterpartyId,
                            counterpartyName);
                    })
                    .ToList()))
            .ToList();
    }
}
