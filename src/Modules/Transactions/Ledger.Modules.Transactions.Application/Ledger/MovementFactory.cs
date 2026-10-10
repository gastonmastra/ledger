using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Transactions.Domain;

namespace Ledger.Modules.Transactions.Application.Ledger;

/// <summary>Builds income, expense and transfer transactions (used to record and to edit them).</summary>
internal sealed class MovementFactory(AccountResolver accounts)
{
    public async Task<Transaction> IncomeAsync(
        Guid userId, Guid walletId, Guid? categoryId, decimal amount, DateTimeOffset occurredAt, string? description,
        CancellationToken cancellationToken)
    {
        var wallet = await accounts.WalletAsync(userId, walletId, cancellationToken);
        var category = await accounts.CategoryAsync(userId, categoryId, LedgerAccountType.Income, cancellationToken);
        var currency = wallet.Currency!.Value;

        return TransactionBuilder
            .Create(userId, occurredAt, TransactionKind.Income, description)
            .Add(wallet.Id, amount, currency)
            .Add(category.Id, -amount, currency)
            .Build();
    }

    public async Task<Transaction> ExpenseAsync(
        Guid userId, Guid walletId, Guid? categoryId, decimal amount, DateTimeOffset occurredAt, string? description,
        CancellationToken cancellationToken)
    {
        var wallet = await accounts.WalletAsync(userId, walletId, cancellationToken);
        var category = await accounts.CategoryAsync(userId, categoryId, LedgerAccountType.Expense, cancellationToken);
        var currency = wallet.Currency!.Value;

        return TransactionBuilder
            .Create(userId, occurredAt, TransactionKind.Expense, description)
            .Add(category.Id, amount, currency)
            .Add(wallet.Id, -amount, currency)
            .Build();
    }

    public async Task<Transaction> TransferAsync(
        Guid userId, Guid fromWalletId, Guid toWalletId, decimal amount, decimal? receivedAmount,
        DateTimeOffset occurredAt, string? description, CancellationToken cancellationToken)
    {
        if (fromWalletId == toWalletId)
            throw new DomainException("transfer.same_wallet", "Choose two different wallets.");

        var from = await accounts.WalletAsync(userId, fromWalletId, cancellationToken);
        var to = await accounts.WalletAsync(userId, toWalletId, cancellationToken);
        var fromCurrency = from.Currency!.Value;
        var toCurrency = to.Currency!.Value;

        var builder = TransactionBuilder.Create(userId, occurredAt, TransactionKind.Transfer, description);

        if (fromCurrency == toCurrency)
        {
            if (receivedAmount is not null && receivedAmount != amount)
                throw new DomainException(
                    "transfer.received_amount_mismatch",
                    "Both wallets use the same currency: the received amount must equal the amount sent.");

            builder
                .Add(from.Id, -amount, fromCurrency)
                .Add(to.Id, amount, toCurrency);
        }
        else
        {
            var received = receivedAmount
                ?? throw new DomainException(
                    "transfer.received_amount_required",
                    $"The wallets use different currencies ({fromCurrency} → {toCurrency}): enter the amount received.");

            // Each currency balances through the currency exchange account.
            var exchange = await accounts.SystemAsync(userId, LedgerAccountPurpose.CurrencyExchange, cancellationToken);

            builder
                .Add(from.Id, -amount, fromCurrency)
                .Add(exchange.Id, amount, fromCurrency)
                .Add(exchange.Id, -received, toCurrency)
                .Add(to.Id, received, toCurrency);
        }

        return builder.Build();
    }
}
