using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Transactions.Domain;

namespace Ledger.Modules.Transactions.Application.Ledger;

public sealed record RecordIncomeCommand(
    Guid UserId,
    Guid WalletId,
    Guid? CategoryId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Description) : ICommand<Guid>;

public sealed record RecordExpenseCommand(
    Guid UserId,
    Guid WalletId,
    Guid? CategoryId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Description) : ICommand<Guid>;

/// <param name="ReceivedAmount">Required when the wallets have different currencies: the amount
/// that arrived to the destination wallet (implies the exchange rate).</param>
public sealed record TransferCommand(
    Guid UserId,
    Guid FromWalletId,
    Guid ToWalletId,
    decimal Amount,
    decimal? ReceivedAmount,
    DateTimeOffset OccurredAt,
    string? Description) : ICommand<Guid>;

internal sealed class RecordIncomeValidator : AbstractValidator<RecordIncomeCommand>
{
    public RecordIncomeValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(19, Money.Decimals, true);
        RuleFor(x => x.Description).MaximumLength(Transaction.DescriptionMaxLength);
    }
}

internal sealed class RecordExpenseValidator : AbstractValidator<RecordExpenseCommand>
{
    public RecordExpenseValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(19, Money.Decimals, true);
        RuleFor(x => x.Description).MaximumLength(Transaction.DescriptionMaxLength);
    }
}

internal sealed class TransferValidator : AbstractValidator<TransferCommand>
{
    public TransferValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(19, Money.Decimals, true);
        RuleFor(x => x.ReceivedAmount).GreaterThan(0).PrecisionScale(19, Money.Decimals, true)
            .When(x => x.ReceivedAmount is not null);
        RuleFor(x => x.ToWalletId).NotEqual(x => x.FromWalletId).WithMessage("Choose two different wallets.");
        RuleFor(x => x.Description).MaximumLength(Transaction.DescriptionMaxLength);
    }
}

internal sealed class RecordHandlers(
    ITransactionsDbContext dbContext,
    AccountResolver accounts,
    LedgerWriter writer)
    : ICommandHandler<RecordIncomeCommand, Guid>,
      ICommandHandler<RecordExpenseCommand, Guid>,
      ICommandHandler<TransferCommand, Guid>
{
    public async Task<Guid> Handle(RecordIncomeCommand command, CancellationToken cancellationToken)
    {
        var wallet = await accounts.WalletAsync(command.UserId, command.WalletId, cancellationToken);
        var category = await accounts.CategoryAsync(
            command.UserId, command.CategoryId, LedgerAccountType.Income, cancellationToken);
        var currency = wallet.Currency!.Value;

        var transaction = TransactionBuilder
            .Create(command.UserId, command.OccurredAt, TransactionKind.Income, command.Description)
            .Add(wallet.Id, command.Amount, currency)
            .Add(category.Id, -command.Amount, currency)
            .Build();

        return await WriteAsync(transaction, cancellationToken);
    }

    public async Task<Guid> Handle(RecordExpenseCommand command, CancellationToken cancellationToken)
    {
        var wallet = await accounts.WalletAsync(command.UserId, command.WalletId, cancellationToken);
        var category = await accounts.CategoryAsync(
            command.UserId, command.CategoryId, LedgerAccountType.Expense, cancellationToken);
        var currency = wallet.Currency!.Value;

        var transaction = TransactionBuilder
            .Create(command.UserId, command.OccurredAt, TransactionKind.Expense, command.Description)
            .Add(category.Id, command.Amount, currency)
            .Add(wallet.Id, -command.Amount, currency)
            .Build();

        return await WriteAsync(transaction, cancellationToken);
    }

    public async Task<Guid> Handle(TransferCommand command, CancellationToken cancellationToken)
    {
        var from = await accounts.WalletAsync(command.UserId, command.FromWalletId, cancellationToken);
        var to = await accounts.WalletAsync(command.UserId, command.ToWalletId, cancellationToken);
        var fromCurrency = from.Currency!.Value;
        var toCurrency = to.Currency!.Value;

        var builder = TransactionBuilder.Create(
            command.UserId, command.OccurredAt, TransactionKind.Transfer, command.Description);

        if (fromCurrency == toCurrency)
        {
            if (command.ReceivedAmount is not null && command.ReceivedAmount != command.Amount)
                throw new DomainException(
                    "transfer.received_amount_mismatch",
                    "Both wallets use the same currency: the received amount must equal the amount sent.");

            builder
                .Add(from.Id, -command.Amount, fromCurrency)
                .Add(to.Id, command.Amount, toCurrency);
        }
        else
        {
            var received = command.ReceivedAmount
                ?? throw new DomainException(
                    "transfer.received_amount_required",
                    $"The wallets use different currencies ({fromCurrency} → {toCurrency}): enter the amount received.");

            // Each currency balances through the currency exchange account.
            var exchange = await accounts.SystemAsync(
                command.UserId, LedgerAccountPurpose.CurrencyExchange, cancellationToken);

            builder
                .Add(from.Id, -command.Amount, fromCurrency)
                .Add(exchange.Id, command.Amount, fromCurrency)
                .Add(exchange.Id, -received, toCurrency)
                .Add(to.Id, received, toCurrency);
        }

        return await WriteAsync(builder.Build(), cancellationToken);
    }

    private Task<Guid> WriteAsync(Transaction transaction, CancellationToken cancellationToken)
        => dbContext.InTransactionAsync(async () =>
        {
            await writer.WriteAsync([transaction], cancellationToken);
            return transaction.Id.Value;
        }, cancellationToken);
}
