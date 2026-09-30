using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Transactions.Domain;

public sealed class TransactionBuilder
{
    private readonly Transaction _transaction;

    private TransactionBuilder(Transaction transaction)
    {
        _transaction = transaction;
    }

    public static TransactionBuilder Create(
        Guid ownerId,
        DateTimeOffset occurredAt,
        TransactionKind kind,
        string? description = null,
        Guid? correlationId = null)
        => new(Transaction.Create(ownerId, occurredAt, kind, description, correlationId));

    public TransactionBuilder Add(AccountId accountId, decimal amount, Currency currency, Guid? counterpartyId = null)
    {
        if (amount != 0)
            _transaction.AddPosting(accountId, amount, currency, counterpartyId);

        return this;
    }

    public Transaction Build()
    {
        _transaction.Validate();

        return _transaction;
    }
}
