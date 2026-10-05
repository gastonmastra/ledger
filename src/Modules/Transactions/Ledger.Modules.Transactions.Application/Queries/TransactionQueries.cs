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
    string? CounterpartyName,
    Guid? CategoryId = null);

/// <param name="Editable">Income, expense or transfer that is current: it can be edited here.</param>
/// <param name="Deletable">Current and of a kind the user deletes here (also balance adjustments).</param>
public sealed record TransactionDto(
    Guid Id,
    Guid EntryId,
    int Revision,
    DateTimeOffset OccurredAt,
    string? Description,
    TransactionKind Kind,
    TransactionStatus Status,
    Guid? CorrelationId,
    string? ChangeReason,
    bool Editable,
    bool Deletable,
    IReadOnlyList<PostingDto> Postings);

/// <param name="IncludeHistory">Also list changed and deleted versions and their reversals.</param>
public sealed record GetTransactionsQuery(
    Guid UserId,
    int Page = 1,
    int PageSize = 20,
    Guid? WalletId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? ContactId = null,
    Guid? CategoryId = null,
    bool IncludeHistory = false) : IQuery<PagedResult<TransactionDto>>;

public sealed record GetTransactionQuery(Guid UserId, Guid TransactionId) : IQuery<TransactionDto>;

internal sealed class TransactionQueryHandlers(ITransactionsDbContext dbContext, TransactionMapper mapper)
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

        if (!query.IncludeHistory)
        {
            transactions = transactions.Where(t =>
                t.ReversedById == null
                && t.Kind != TransactionKind.Reversal
                && t.Kind != TransactionKind.OpeningBalanceReversal);
        }

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
            var contactPersonIds = dbContext.Counterparties
                .Where(c => c.OwnerId == query.UserId
                            && c.Kind == CounterpartyKind.Contact
                            && c.SourceId == contactId)
                .Select(c => c.PersonId);

            // Older postings may carry a person id that was later merged into this contact's user.
            var personIds = dbContext.Counterparties
                .Where(c => c.OwnerId == query.UserId
                            && (contactPersonIds.Contains(c.PersonId)
                                || (c.MergedIntoPersonId != null && contactPersonIds.Contains(c.MergedIntoPersonId.Value))))
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
            await mapper.ToDtosAsync(query.UserId, items, cancellationToken),
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

        return (await mapper.ToDtosAsync(query.UserId, [transaction], cancellationToken))[0];
    }
}

/// <summary>Maps transactions to DTOs with account and counterparty names and their status.</summary>
internal sealed class TransactionMapper(ITransactionsDbContext dbContext)
{
    public async Task<IReadOnlyList<TransactionDto>> ToDtosAsync(
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

        // A reversed transaction was "changed" when its reversal came with a new revision, else "deleted".
        var reversalIds = transactions.Where(t => t.ReversedById != null).Select(t => t.ReversedById!.Value).ToList();
        var reversalActions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => reversalIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.ChangeAction, cancellationToken);

        return transactions
            .Select(t => new TransactionDto(
                t.Id.Value,
                t.EntryId.Value,
                t.Revision,
                t.OccurredAt,
                t.Description,
                t.Kind,
                StatusOf(t, reversalActions),
                t.CorrelationId,
                t.ChangeReason,
                t.IsCurrent && Ledger.RevisionHandlers.Editable.Contains(t.Kind),
                t.IsCurrent && Ledger.RevisionHandlers.Deletable.Contains(t.Kind),
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
                            counterpartyName,
                            account?.Purpose == LedgerAccountPurpose.Category ? account.ReferenceId : null);
                    })
                    .ToList()))
            .ToList();
    }

    private static TransactionStatus StatusOf(Transaction transaction, IReadOnlyDictionary<TransactionId, ChangeAction?> reversalActions)
    {
        if (transaction.IsReversal)
            return TransactionStatus.Reversal;

        if (transaction.ReversedById is not { } reversal)
            return TransactionStatus.Current;

        return reversalActions.GetValueOrDefault(reversal) == ChangeAction.Delete
            ? TransactionStatus.Deleted
            : TransactionStatus.Changed;
    }
}
