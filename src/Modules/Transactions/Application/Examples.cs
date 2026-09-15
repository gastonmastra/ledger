using Ledger.Modules.Transactions.Domain;

namespace Ledger.Modules.Transactions.Application;

public static class SimpleTransactionExample
{
    public static Transaction CreateLunch(
        AccountId foodAccountId,
        AccountId santanderAccountId
    )
    {
        return TransactionBuilder
            .Create(
                DateTimeOffset.UtcNow,
                "Lunch")
            .Add(foodAccountId, 20m)
            .Add(santanderAccountId, -20m)
            .Build();
    }
}

public static class SharedTransactionExample
{
    public static Transaction CreateDinner(
        AccountId foodAccountId,
        AccountId sofiaReceivableAccountId,
        AccountId santanderAccountId)
    {
        return TransactionBuilder
            .Create(
                DateTimeOffset.UtcNow,
                "Dinner")
            .Add(foodAccountId, 50m)
            .Add(sofiaReceivableAccountId, 50m)
            .Add(santanderAccountId, -100m)
            .Build();
    }
}

public static class DetailedTransactionExample
{
    public static Transaction CreateShopping(
        AccountId groceriesAccountId,
        AccountId cleaningAccountId,
        AccountId toiletriesAccountId,
        AccountId santanderAccountId)
    {
        return TransactionBuilder
            .Create(
                DateTimeOffset.UtcNow,
                "Shopping")
            .Add(groceriesAccountId, 30m)
            .Add(cleaningAccountId, 20m)
            .Add(toiletriesAccountId, 10m)
            .Add(santanderAccountId, -60m)
            .Build();
    }
}