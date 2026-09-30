using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Transactions.Domain;

/// <summary>
/// A signed, non-zero amount applied to one account. Positive amounts are debits and negative
/// amounts are credits. Receivable/payable postings carry the counterparty they refer to.
/// </summary>
public sealed class Posting
{
    private Posting(
        PostingId id,
        TransactionId transactionId,
        AccountId accountId,
        decimal amount,
        Currency currency,
        Guid? counterpartyId)
    {
        Id = id;
        TransactionId = transactionId;
        AccountId = accountId;
        Amount = amount;
        Currency = currency;
        CounterpartyId = counterpartyId;
    }

    public PostingId Id { get; private set; }
    public TransactionId TransactionId { get; private set; }
    public AccountId AccountId { get; private set; }
    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; }
    public Guid? CounterpartyId { get; private set; }

    internal static Posting Create(
        TransactionId transactionId,
        AccountId accountId,
        decimal amount,
        Currency currency,
        Guid? counterpartyId)
    {
        if (amount == 0)
            throw new DomainException("posting.zero_amount", "Posting amount cannot be zero.");

        Money.EnsureCents(amount);

        return new Posting(PostingId.New(), transactionId, accountId, amount, currency, counterpartyId);
    }
}
