namespace Ledger.Modules.Transactions.Application.GetTransaction;

using Ledger.Modules.Transactions.Domain;
using Ledger.Modules.Transactions.Infrastructure;
using Microsoft.EntityFrameworkCore;

public sealed class GetTransactionHandler(
    TransactionsDbContext dbContext)
{
    private readonly TransactionsDbContext _dbContext = dbContext;

    public async Task<TransactionDto?> Handle(
        GetTransactionQuery query,
        CancellationToken cancellationToken)
    {
        var transactionId = new TransactionId(query.TransactionId);

        var transaction = await _dbContext.Transactions
            .AsNoTracking()
            .Include(x => x.Postings)
            .SingleOrDefaultAsync(
                x => x.Id == transactionId,
                cancellationToken);

        if (transaction is null)
            return null;

        return new TransactionDto(
            transaction.Id.Value,
            transaction.OccurredAt,
            transaction.Description,
            transaction.Postings
                .Select(posting =>
                    new PostingDto(
                        posting.AccountId.Value,
                        posting.Amount))
                .ToList());
    }
}