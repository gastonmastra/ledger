namespace Ledger.Modules.Transactions.Application.CreateTransaction;

using Ledger.Modules.Transactions.Domain;
using Ledger.Modules.Transactions.Infrastructure;

public sealed class CreateTransactionHandler(
    TransactionsDbContext dbContext)
{
    private readonly TransactionsDbContext _dbContext = dbContext;

    public async Task<TransactionId> Handle(
        CreateTransactionCommand command,
        CancellationToken cancellationToken)
    {
        var builder = TransactionBuilder.Create(
            command.OccurredAt,
            command.Description);

        foreach (var posting in command.Postings)
        {
            builder.Add(
                new AccountId(posting.AccountId),
                posting.Amount);
        }

        var transaction = builder.Build();

        _dbContext.Transactions.Add(transaction);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return transaction.Id;
    }
}