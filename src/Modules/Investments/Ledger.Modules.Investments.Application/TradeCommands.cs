using System.Globalization;
using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Investments.Domain;
using Ledger.Modules.Transactions.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Investments.Application;

/// <summary>
/// Records a purchase, a sale or an already owned holding. The ledger records it asynchronously:
/// the result is a <see cref="TradeStatus.Pending"/> trade.
/// </summary>
/// <param name="WalletId">Pays (buy) or receives (sell) the money; null for <see cref="TradeKind.Opening"/>.</param>
/// <param name="WalletAmount">Money in the wallet's currency, required when it differs from the asset currency.
/// The ledger owns the wallets: it checks the wallet and its currency (and refuses the trade if needed).</param>
/// <param name="CounterPrice">Optional price of the other side: the sell price on a purchase, the buy price on a sale.</param>
public sealed record RecordTradeCommand(
    Guid UserId,
    TradeKind Kind,
    Guid AssetId,
    decimal Quantity,
    decimal UnitPrice,
    Guid? WalletId,
    decimal? WalletAmount,
    decimal? CounterPrice,
    DateTimeOffset OccurredAt,
    string? Description) : ICommand<TradeDto>;

/// <summary>Internal: the ledger recorded the trade.</summary>
public sealed record CompleteTradeCommand(Guid TradeId, string? WalletCurrency) : ICommand;

/// <summary>Internal: the ledger refused the trade (e.g. not enough money, a locked period).</summary>
public sealed record RejectTradeCommand(Guid TradeId, string Reason) : ICommand;

public sealed record GetTradesQuery(Guid UserId, Guid? AssetId, int Page = 1, int PageSize = 50)
    : IQuery<PagedResult<TradeDto>>;

public sealed record GetTradeQuery(Guid UserId, Guid TradeId) : IQuery<TradeDto>;

internal sealed class RecordTradeValidator : AbstractValidator<RecordTradeCommand>
{
    public RecordTradeValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Quantity).GreaterThan(0).PrecisionScale(Quantities.Precision, Quantities.Decimals, true);
        RuleFor(x => x.UnitPrice).GreaterThan(0).PrecisionScale(Prices.Precision, Prices.Decimals, true);
        RuleFor(x => x.CounterPrice).GreaterThan(0).PrecisionScale(Prices.Precision, Prices.Decimals, true)
            .When(x => x.CounterPrice is not null);
        RuleFor(x => x.WalletAmount).GreaterThan(0).PrecisionScale(19, Money.Decimals, true)
            .When(x => x.WalletAmount is not null);
        RuleFor(x => x.WalletId).NotNull().When(x => x.Kind != TradeKind.Opening)
            .WithMessage("Choose the wallet that pays or receives the money.");
        RuleFor(x => x.WalletId).Null().When(x => x.Kind == TradeKind.Opening)
            .WithMessage("An already owned holding doesn't use a wallet.");
        RuleFor(x => x.Description).MaximumLength(Trade.DescriptionMaxLength);
        RuleFor(x => x.OccurredAt).NotInFuture();
    }
}

internal sealed class TradeHandlers(
    IInvestmentsDbContext dbContext,
    IInvestmentsEventBus eventBus)
    : ICommandHandler<RecordTradeCommand, TradeDto>,
      ICommandHandler<CompleteTradeCommand>,
      ICommandHandler<RejectTradeCommand>,
      IQueryHandler<GetTradesQuery, PagedResult<TradeDto>>,
      IQueryHandler<GetTradeQuery, TradeDto>
{
    public async Task<TradeDto> Handle(RecordTradeCommand command, CancellationToken cancellationToken)
    {
        var asset = await AssetLookup.GetOwnedAsync(dbContext, command.UserId, command.AssetId, cancellationToken);

        var trade = asset.StartTrade(
            command.Kind,
            command.Quantity,
            command.UnitPrice,
            command.OccurredAt,
            command.Description,
            command.WalletId,
            command.WalletAmount,
            command.CounterPrice);

        dbContext.Trades.Add(trade);

        await eventBus.PublishAsync(
            new InvestmentPostingRequested(
                trade.Id,
                trade.Kind.ToString(),
                trade.OwnerId,
                asset.Id,
                asset.Currency.ToString(),
                trade.OccurredAt,
                trade.Description ?? DefaultDescription(trade, asset),
                trade.Total,
                trade.CostRemoved,
                trade.WalletId,
                trade.WalletAmount),
            cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another trade of the same asset was started at the same time.
            throw new DomainException(
                "investment.trade_pending",
                $"Another trade of '{asset.Name}' is still being recorded. Try again in a moment.");
        }

        return trade.ToDto(asset);
    }

    public async Task Handle(CompleteTradeCommand command, CancellationToken cancellationToken)
    {
        var trade = await dbContext.Trades.SingleAsync(t => t.Id == command.TradeId, cancellationToken);

        if (!trade.Complete(command.WalletCurrency is null ? null : Currencies.Parse(command.WalletCurrency)))
            return;

        var asset = await dbContext.Assets.SingleAsync(a => a.Id == trade.AssetId, cancellationToken);
        asset.Apply(trade);

        dbContext.Prices.AddRange(trade.Prices());

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(RejectTradeCommand command, CancellationToken cancellationToken)
    {
        var trade = await dbContext.Trades.SingleAsync(t => t.Id == command.TradeId, cancellationToken);

        if (!trade.Reject(command.Reason))
            return;

        var asset = await dbContext.Assets.SingleAsync(a => a.Id == trade.AssetId, cancellationToken);
        asset.Release(trade);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<TradeDto>> Handle(GetTradesQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var trades = dbContext.Trades
            .AsNoTracking()
            .Where(t => t.OwnerId == query.UserId && (query.AssetId == null || t.AssetId == query.AssetId));

        var total = await trades.CountAsync(cancellationToken);

        var items = await trades
            .OrderByDescending(t => t.OccurredAt).ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var assetIds = items.Select(t => t.AssetId).Distinct().ToList();
        var assets = await dbContext.Assets
            .AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        return new PagedResult<TradeDto>(
            items.Select(t => t.ToDto(assets[t.AssetId])).ToList(), page, pageSize, total);
    }

    public async Task<TradeDto> Handle(GetTradeQuery query, CancellationToken cancellationToken)
    {
        var trade = await dbContext.Trades.AsNoTracking().SingleOrDefaultAsync(t => t.Id == query.TradeId, cancellationToken);

        if (trade is null || trade.OwnerId != query.UserId)
            throw new NotFoundException("Trade", query.TradeId);

        var asset = await dbContext.Assets.AsNoTracking().SingleAsync(a => a.Id == trade.AssetId, cancellationToken);

        return trade.ToDto(asset);
    }

    /// <summary>Description of the ledger transaction when the user gave none, e.g. "Buy 0.5 BTC".</summary>
    private static string DefaultDescription(Trade trade, Asset asset)
    {
        var verb = trade.Kind switch
        {
            TradeKind.Buy => "Buy",
            TradeKind.Sell => "Sell",
            _ => "Already owned:"
        };

        return $"{verb} {trade.Quantity.ToString("0.############", CultureInfo.InvariantCulture)} {asset.Symbol ?? asset.Name}";
    }
}
