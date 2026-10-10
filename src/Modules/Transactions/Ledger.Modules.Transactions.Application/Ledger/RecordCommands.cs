using FluentValidation;
using Ledger.BuildingBlocks.Domain;
using Ledger.BuildingBlocks.Application;
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
        RuleFor(x => x.OccurredAt).NotInFuture();
    }
}

internal sealed class RecordExpenseValidator : AbstractValidator<RecordExpenseCommand>
{
    public RecordExpenseValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(19, Money.Decimals, true);
        RuleFor(x => x.Description).MaximumLength(Transaction.DescriptionMaxLength);
        RuleFor(x => x.OccurredAt).NotInFuture();
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
        RuleFor(x => x.OccurredAt).NotInFuture();
    }
}

internal sealed class RecordHandlers(
    ITransactionsDbContext dbContext,
    MovementFactory movements,
    LedgerWriter writer)
    : ICommandHandler<RecordIncomeCommand, Guid>,
      ICommandHandler<RecordExpenseCommand, Guid>,
      ICommandHandler<TransferCommand, Guid>
{
    public async Task<Guid> Handle(RecordIncomeCommand command, CancellationToken cancellationToken)
        => await WriteAsync(
            await movements.IncomeAsync(
                command.UserId, command.WalletId, command.CategoryId, command.Amount, command.OccurredAt,
                command.Description, cancellationToken),
            cancellationToken);

    public async Task<Guid> Handle(RecordExpenseCommand command, CancellationToken cancellationToken)
        => await WriteAsync(
            await movements.ExpenseAsync(
                command.UserId, command.WalletId, command.CategoryId, command.Amount, command.OccurredAt,
                command.Description, cancellationToken),
            cancellationToken);

    public async Task<Guid> Handle(TransferCommand command, CancellationToken cancellationToken)
        => await WriteAsync(
            await movements.TransferAsync(
                command.UserId, command.FromWalletId, command.ToWalletId, command.Amount, command.ReceivedAmount,
                command.OccurredAt, command.Description, cancellationToken),
            cancellationToken);

    private Task<Guid> WriteAsync(Transaction transaction, CancellationToken cancellationToken)
        => dbContext.InTransactionAsync(async () =>
        {
            await writer.WriteAsync([transaction], cancellationToken);
            return transaction.Id.Value;
        }, cancellationToken);
}
