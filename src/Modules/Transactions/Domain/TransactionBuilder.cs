namespace Ledger.Modules.Transactions.Domain;

public sealed class TransactionBuilder
{
    private readonly Transaction _transaction;

    private TransactionBuilder(
        Transaction transaction
    )
    {
        _transaction = transaction;
    }

    public static TransactionBuilder Create(
        DateTimeOffset occurredAt,
        string? description = null
    )
    {
        return new TransactionBuilder(
            Transaction.Create(
                occurredAt,
                description
            )
        );
    }

    public TransactionBuilder Add(
        AccountId accountId,
        decimal amount
    )
    {
        _transaction.AddPosting(
            accountId,
            amount
        );

        return this;
    }

    public Transaction Build()
    {
        _transaction.Validate();

        return _transaction;
    }
}