using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Transactions.Contracts;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Ledger;

/// <param name="WalletCurrency">Currency of the wallet used, when there is one.</param>
public sealed record InvestmentPostingResult(bool Succeeded, string? RejectionReason, string? WalletCurrency);

/// <summary>Records an investment trade (buy, sell or already owned holding) in the user's books.</summary>
public sealed record PostInvestmentTradeCommand(InvestmentPostingRequested Request) : ICommand<InvestmentPostingResult>;

/// <summary>
/// The ledger records investments at cost: the Investment account of the asset holds what was paid.
/// A sale removes the average cost of the units sold; the difference with what was received goes to
/// Investment gains. Money in another currency goes through Currency exchange, like transfers.
/// </summary>
internal sealed class InvestmentHandlers(
    ITransactionsDbContext dbContext,
    AccountResolver accounts,
    LedgerWriter writer)
    : ICommandHandler<PostInvestmentTradeCommand, InvestmentPostingResult>
{
    public async Task<InvestmentPostingResult> Handle(PostInvestmentTradeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        var posted = await dbContext.Transactions.AsNoTracking()
            .Where(x => x.CorrelationId == request.TradeId)
            .AnyAsync(cancellationToken);

        if (posted)
            return new InvestmentPostingResult(true, null, await WalletCurrencyAsync(request, cancellationToken));

        try
        {
            return await dbContext.InTransactionAsync(async () =>
            {
                var (transaction, walletCurrency) = await BuildAsync(request, cancellationToken);

                await writer.WriteAsync([transaction], cancellationToken);

                return new InvestmentPostingResult(true, null, walletCurrency?.ToString());
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException)
        {
            // Business rejection (e.g. not enough money, a locked period): nothing is written.
            dbContext.DiscardLedgerChanges();
            return new InvestmentPostingResult(false, ex.Message, null);
        }
    }

    private async Task<(Transaction Transaction, Currency? WalletCurrency)> BuildAsync(
        InvestmentPostingRequested request,
        CancellationToken cancellationToken)
    {
        var currency = Currencies.Parse(request.AssetCurrency);
        var investment = await accounts.InvestmentAsync(request.OwnerId, request.AssetId, cancellationToken);

        var kind = request.Kind switch
        {
            InvestmentPostingKinds.Buy => TransactionKind.InvestmentBuy,
            InvestmentPostingKinds.Sell => TransactionKind.InvestmentSell,
            InvestmentPostingKinds.Opening => TransactionKind.InvestmentOpening,
            _ => throw new DomainException("ledger.unknown_kind", $"Unknown investment trade '{request.Kind}'.")
        };

        var builder = TransactionBuilder.Create(
            request.OwnerId, request.OccurredAt, kind, request.Description, request.TradeId);

        if (kind == TransactionKind.InvestmentOpening)
        {
            var openingBalances = await accounts.SystemAsync(
                request.OwnerId, LedgerAccountPurpose.OpeningBalances, cancellationToken);

            builder
                .Add(investment.Id, request.Total, currency)
                .Add(openingBalances.Id, -request.Total, currency);

            return (builder.Build(), null);
        }

        if (request.WalletId is not { } walletId)
            throw new DomainException("ledger.wallet_required", "Choose the wallet that pays or receives the money.");

        var wallet = await accounts.WalletAsync(request.OwnerId, walletId, cancellationToken);

        // Money that leaves (buy, negative) or enters (sell, positive) the user's pocket, in the asset currency.
        var cash = kind == TransactionKind.InvestmentBuy ? -request.Total : request.Total;

        if (kind == TransactionKind.InvestmentBuy)
        {
            builder.Add(investment.Id, request.Total, currency);
        }
        else
        {
            var gains = await accounts.SystemAsync(request.OwnerId, LedgerAccountPurpose.InvestmentGains, cancellationToken);

            builder
                .Add(investment.Id, -request.CostRemoved, currency)
                .Add(gains.Id, request.CostRemoved - request.Total, currency);
        }

        if (wallet.Currency == currency)
        {
            builder.Add(wallet.Id, cash, currency);
        }
        else
        {
            if (request.WalletAmount is not { } walletAmount || walletAmount <= 0)
                throw new DomainException(
                    "ledger.currency_mismatch",
                    $"Wallet '{wallet.Name}' holds {wallet.Currency} and the asset is quoted in {currency}: enter the amount in {wallet.Currency}.");

            var exchange = await accounts.SystemAsync(request.OwnerId, LedgerAccountPurpose.CurrencyExchange, cancellationToken);
            var walletCurrency = wallet.Currency!.Value;
            var walletCash = Math.Sign(cash) * walletAmount;

            builder
                .Add(exchange.Id, cash, currency)
                .Add(exchange.Id, -walletCash, walletCurrency)
                .Add(wallet.Id, walletCash, walletCurrency);
        }

        return (builder.Build(), wallet.Currency);
    }

    private async Task<string?> WalletCurrencyAsync(InvestmentPostingRequested request, CancellationToken cancellationToken)
        => request.WalletId is { } walletId
            ? (await dbContext.LedgerAccounts.AsNoTracking()
                .Where(x => x.Purpose == LedgerAccountPurpose.Wallet && x.ReferenceId == walletId)
                .Select(x => x.Currency)
                .FirstOrDefaultAsync(cancellationToken))?.ToString()
            : null;
}
