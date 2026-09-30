using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Transactions.Domain;

namespace Ledger.UnitTests;

public class TransactionTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly AccountId Bank = new(Guid.NewGuid());
    private static readonly AccountId Food = new(Guid.NewGuid());
    private static readonly AccountId Exchange = new(Guid.NewGuid());
    private static readonly AccountId Dollars = new(Guid.NewGuid());

    [Fact]
    public void Balanced_postings_build_a_transaction()
    {
        var transaction = TransactionBuilder
            .Create(Owner, DateTimeOffset.UtcNow, TransactionKind.Expense, "Lunch")
            .Add(Food, 20, Currency.EUR)
            .Add(Bank, -20, Currency.EUR)
            .Build();

        Assert.Equal(2, transaction.Postings.Count);
        Assert.Equal(0, transaction.Postings.Sum(p => p.Amount));
    }

    [Fact]
    public void Unbalanced_postings_are_rejected()
    {
        var builder = TransactionBuilder
            .Create(Owner, DateTimeOffset.UtcNow, TransactionKind.Expense)
            .Add(Food, 20, Currency.EUR)
            .Add(Bank, -19.99m, Currency.EUR);

        var error = Assert.Throws<DomainException>(builder.Build);
        Assert.Equal("transaction.unbalanced", error.Code);
    }

    [Fact]
    public void Each_currency_must_balance_on_its_own()
    {
        // 100 USD out, 92 EUR in: the totals differ but each currency balances via the exchange account.
        var transaction = TransactionBuilder
            .Create(Owner, DateTimeOffset.UtcNow, TransactionKind.Transfer)
            .Add(Dollars, -100, Currency.USD)
            .Add(Exchange, 100, Currency.USD)
            .Add(Exchange, -92, Currency.EUR)
            .Add(Bank, 92, Currency.EUR)
            .Build();

        Assert.Equal(4, transaction.Postings.Count);

        var mixed = TransactionBuilder
            .Create(Owner, DateTimeOffset.UtcNow, TransactionKind.Transfer)
            .Add(Dollars, -100, Currency.USD)
            .Add(Bank, 100, Currency.EUR);

        Assert.Throws<DomainException>(mixed.Build);
    }

    [Fact]
    public void A_transaction_needs_two_postings()
    {
        var builder = TransactionBuilder
            .Create(Owner, DateTimeOffset.UtcNow, TransactionKind.Income)
            .Add(Bank, 10, Currency.EUR);

        Assert.Equal("transaction.too_few_postings", Assert.Throws<DomainException>(builder.Build).Code);
    }

    [Fact]
    public void Amounts_have_at_most_two_decimals()
    {
        var transaction = Transaction.Create(Owner, DateTimeOffset.UtcNow, TransactionKind.Expense, null, null);

        Assert.Throws<DomainException>(() => transaction.AddPosting(Bank, 0.001m, Currency.EUR));
        Assert.Throws<DomainException>(() => transaction.AddPosting(Bank, 0, Currency.EUR));
    }
}
