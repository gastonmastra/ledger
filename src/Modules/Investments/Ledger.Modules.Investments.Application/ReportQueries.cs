using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Investments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Investments.Application;

/// <param name="Value">Market value of the holdings now.</param>
/// <param name="Cost">What the holdings cost (cost basis).</param>
/// <param name="UnrealizedGain">Value − Cost.</param>
/// <param name="RealizedGain">Gains and losses of every sale so far.</param>
public sealed record InvestmentsTotalDto(
    string Currency,
    decimal Value,
    decimal Cost,
    decimal UnrealizedGain,
    decimal RealizedGain);

public sealed record GetInvestmentsSummaryQuery(Guid UserId) : IQuery<IReadOnlyList<InvestmentsTotalDto>>;

/// <param name="Value">Market value of the holdings at the end of the bucket.</param>
/// <param name="Cost">Cost basis of the holdings at the end of the bucket.</param>
public sealed record InvestmentsBucketDto(DateTimeOffset Start, decimal Value, decimal Cost);

public sealed record InvestmentsAssetValueDto(Guid AssetId, string Name, string? Symbol, decimal Value);

/// <param name="Buckets">Same buckets as the ledger report for the same period and time zone.</param>
/// <param name="AssetValues">Value of each holding at the end of the period.</param>
/// <param name="RealizedGain">Gains and losses of the sales made in the period.</param>
public sealed record InvestmentsReportDto(
    string Currency,
    IReadOnlyList<string> Currencies,
    ReportGranularity Granularity,
    IReadOnlyList<InvestmentsBucketDto> Buckets,
    IReadOnlyList<InvestmentsAssetValueDto> AssetValues,
    decimal RealizedGain);

/// <param name="Currency">Null: the first currency the user invests in.</param>
public sealed record GetInvestmentsReportQuery(
    Guid UserId,
    DateTimeOffset From,
    DateTimeOffset To,
    string? Currency,
    string? TimeZone) : IQuery<InvestmentsReportDto>;

internal sealed class GetInvestmentsReportValidator : AbstractValidator<GetInvestmentsReportQuery>
{
    public GetInvestmentsReportValidator()
    {
        RuleFor(x => x.To).GreaterThan(x => x.From);
        RuleFor(x => x).Must(x => x.To - x.From <= TimeSpan.FromDays(3 * 366))
            .WithMessage("The period can be at most three years.");
        RuleFor(x => x.Currency).Must(Currencies.IsSupported).When(x => x.Currency is not null)
            .WithMessage("Unsupported currency.");
    }
}

internal sealed class ReportHandlers(IInvestmentsDbContext dbContext)
    : IQueryHandler<GetInvestmentsSummaryQuery, IReadOnlyList<InvestmentsTotalDto>>,
      IQueryHandler<GetInvestmentsReportQuery, InvestmentsReportDto>
{
    public async Task<IReadOnlyList<InvestmentsTotalDto>> Handle(
        GetInvestmentsSummaryQuery query,
        CancellationToken cancellationToken)
    {
        var assets = await dbContext.Assets.AsNoTracking()
            .Where(a => a.OwnerId == query.UserId)
            .ToListAsync(cancellationToken);

        var prices = (await dbContext.Prices.AsNoTracking()
                .Where(p => p.OwnerId == query.UserId)
                .ToListAsync(cancellationToken))
            .ToLookup(p => p.AssetId);

        var now = DateTimeOffset.UtcNow;

        return assets
            .Where(a => a.Quantity != 0 || a.RealizedGain != 0)
            .Select(a => (Asset: a, Dto: a.ToDto(prices[a.Id].ToList(), now)))
            .GroupBy(x => x.Asset.Currency)
            .OrderBy(g => g.Key)
            .Select(g => new InvestmentsTotalDto(
                g.Key.ToString(),
                g.Sum(x => x.Dto.MarketValue),
                g.Sum(x => x.Asset.CostBasis),
                g.Sum(x => x.Dto.UnrealizedGain),
                g.Sum(x => x.Asset.RealizedGain)))
            .ToList();
    }

    /// <summary>
    /// Value of the holdings over time: at the end of each bucket, the quantity owned (from the
    /// recorded trades) × the valuation price known at that moment.
    /// </summary>
    public async Task<InvestmentsReportDto> Handle(GetInvestmentsReportQuery query, CancellationToken cancellationToken)
    {
        var zone = ReportBuckets.FindZone(query.TimeZone);

        var assets = await dbContext.Assets.AsNoTracking()
            .Where(a => a.OwnerId == query.UserId)
            .ToListAsync(cancellationToken);

        var currencies = assets.Select(a => a.Currency).Distinct().Order().ToList();

        var currency = query.Currency is not null
            ? Currencies.Parse(query.Currency)
            : currencies.Count > 0 ? currencies[0] : Currency.EUR;

        var inCurrency = assets.Where(a => a.Currency == currency).ToList();
        var assetIds = inCurrency.Select(a => a.Id).ToList();

        var trades = (await dbContext.Trades.AsNoTracking()
                .Where(t => assetIds.Contains(t.AssetId) && t.Status == TradeStatus.Completed && t.OccurredAt <= query.To)
                .ToListAsync(cancellationToken))
            .ToLookup(t => t.AssetId);

        var prices = (await dbContext.Prices.AsNoTracking()
                .Where(p => assetIds.Contains(p.AssetId) && p.OccurredAt <= query.To)
                .ToListAsync(cancellationToken))
            .ToLookup(p => p.AssetId);

        var granularity = ReportBuckets.GranularityFor(query.From, query.To);
        var starts = ReportBuckets.Starts(query.From, query.To, granularity, zone);
        var buckets = new List<InvestmentsBucketDto>();

        for (var b = 0; b < starts.Count; b++)
        {
            var end = ReportBuckets.End(starts, b, query.To);
            decimal value = 0, cost = 0;

            foreach (var asset in inCurrency)
            {
                var (assetValue, assetCost) = HoldingAt(trades[asset.Id], prices[asset.Id], end);
                value += assetValue;
                cost += assetCost;
            }

            buckets.Add(new InvestmentsBucketDto(starts[b], value, cost));
        }

        var periodEnd = query.To.AddTicks(1);

        var assetValues = inCurrency
            .Select(a => new InvestmentsAssetValueDto(a.Id, a.Name, a.Symbol, HoldingAt(trades[a.Id], prices[a.Id], periodEnd).Value))
            .Where(a => a.Value != 0)
            .OrderByDescending(a => a.Value)
            .ToList();

        var realized = trades
            .SelectMany(g => g)
            .Where(t => t.OccurredAt >= query.From)
            .Sum(t => t.RealizedGain);

        return new InvestmentsReportDto(
            currency.ToString(),
            currencies.Select(c => c.ToString()).ToList(),
            granularity,
            buckets,
            assetValues,
            realized);
    }

    /// <summary>Market value and cost of one holding with the trades and prices before <paramref name="before"/>.</summary>
    private static (decimal Value, decimal Cost) HoldingAt(
        IEnumerable<Trade> trades,
        IEnumerable<AssetPrice> prices,
        DateTimeOffset before)
    {
        decimal quantity = 0, cost = 0;

        foreach (var trade in trades.Where(t => t.OccurredAt < before).OrderBy(t => t.OccurredAt).ThenBy(t => t.CreatedAt))
        {
            if (trade.Kind == TradeKind.Sell)
            {
                quantity -= trade.Quantity;
                cost -= trade.CostRemoved;
            }
            else
            {
                quantity += trade.Quantity;
                cost += trade.Total;
            }
        }

        if (quantity == 0)
            return (0, cost);

        var price = Valuation.PriceAt(prices, before, quantity, cost);

        return (Valuation.Value(quantity, cost, price), cost);
    }
}
