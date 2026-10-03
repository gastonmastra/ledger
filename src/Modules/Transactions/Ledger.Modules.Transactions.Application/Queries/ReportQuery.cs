using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Queries;

/// <param name="NetWorth">Wallets + owed to me − I owe + pending, at the end of the bucket.</param>
public sealed record ReportBucketDto(DateTimeOffset Start, decimal Income, decimal Expenses, decimal NetWorth);

public sealed record CategoryAmountDto(Guid AccountId, string Name, string Purpose, decimal Amount);

public sealed record ReportWalletDto(Guid WalletId, string Name, decimal Balance);

public sealed record ReportDto(
    string Currency,
    IReadOnlyList<string> Currencies,
    ReportGranularity Granularity,
    decimal TotalIncome,
    decimal TotalExpenses,
    IReadOnlyList<ReportBucketDto> Buckets,
    IReadOnlyList<CategoryAmountDto> SpendingByCategory,
    IReadOnlyList<ReportWalletDto> WalletBalances);

/// <param name="Currency">Null: the currency with the most activity in the period.</param>
/// <param name="TimeZone">IANA time zone used to cut days and months (e.g. "Europe/Madrid").</param>
public sealed record GetReportQuery(
    Guid UserId,
    DateTimeOffset From,
    DateTimeOffset To,
    string? Currency,
    string? TimeZone) : IQuery<ReportDto>;

internal sealed class GetReportValidator : AbstractValidator<GetReportQuery>
{
    public GetReportValidator()
    {
        RuleFor(x => x.To).GreaterThan(x => x.From);
        RuleFor(x => x).Must(x => x.To - x.From <= TimeSpan.FromDays(3 * 366))
            .WithMessage("The period can be at most three years.");
        RuleFor(x => x.Currency).Must(Currencies.IsSupported).When(x => x.Currency is not null)
            .WithMessage("Unsupported currency.");
    }
}

/// <summary>
/// Figures for the dashboard charts. Income and expenses only count income and expense accounts
/// (opening balances, currency exchange and balance adjustments are excluded).
/// </summary>
internal sealed class GetReportHandler(ITransactionsDbContext dbContext) : IQueryHandler<GetReportQuery, ReportDto>
{
    private static readonly LedgerAccountPurpose[] NetWorthPurposes =
    [
        LedgerAccountPurpose.Wallet,
        LedgerAccountPurpose.Receivables,
        LedgerAccountPurpose.Payables,
        LedgerAccountPurpose.PendingSettlements
    ];

    public async Task<ReportDto> Handle(GetReportQuery query, CancellationToken cancellationToken)
    {
        var zone = ReportBuckets.FindZone(query.TimeZone);

        var accounts = await dbContext.LedgerAccounts
            .AsNoTracking()
            .Where(a => a.OwnerId == query.UserId)
            .ToDictionaryAsync(a => a.Id, cancellationToken);
        var accountIds = accounts.Keys.ToList();

        var postings = dbContext.Postings.Where(p => accountIds.Contains(p.AccountId));

        var currencies = (await postings.Select(p => p.Currency).Distinct().ToListAsync(cancellationToken))
            .Order()
            .ToList();

        var currency = query.Currency is not null
            ? Currencies.Parse(query.Currency)
            : await MostActiveCurrencyAsync(postings, query, currencies, cancellationToken);

        var rows = await (
                from p in postings
                join t in dbContext.Transactions on p.TransactionId equals t.Id
                where p.Currency == currency && t.OccurredAt <= query.To
                orderby t.OccurredAt
                select new
                {
                    p.AccountId,
                    p.Amount,
                    t.OccurredAt,
                    // Changed or deleted versions and their reversals cancel out: they never count as income or spending.
                    Current = t.ReversedById == null
                              && t.Kind != TransactionKind.Reversal
                              && t.Kind != TransactionKind.OpeningBalanceReversal
                })
            .ToListAsync(cancellationToken);

        var granularity = ReportBuckets.GranularityFor(query.From, query.To);
        var bucketStarts = ReportBuckets.Starts(query.From, query.To, granularity, zone);
        var buckets = new List<ReportBucketDto>();
        var netWorth = 0m;
        var index = 0;

        for (var b = 0; b < bucketStarts.Count; b++)
        {
            var start = bucketStarts[b];
            var end = ReportBuckets.End(bucketStarts, b, query.To);
            decimal income = 0, expenses = 0;

            for (; index < rows.Count && rows[index].OccurredAt < end; index++)
            {
                var row = rows[index];
                var account = accounts[row.AccountId];

                if (NetWorthPurposes.Contains(account.Purpose))
                    netWorth += row.Amount;

                if (row.OccurredAt < start || !row.Current)
                    continue; // before the period, or not the current version: only affects net worth

                if (account.Type == LedgerAccountType.Income)
                    income -= row.Amount;
                else if (account.Type == LedgerAccountType.Expense)
                    expenses += row.Amount;
            }

            buckets.Add(new ReportBucketDto(start, income, expenses, netWorth));
        }

        var inPeriod = rows.Where(r => r.OccurredAt >= query.From).ToList();

        var spending = inPeriod
            .Where(r => r.Current && accounts[r.AccountId].Type == LedgerAccountType.Expense)
            .GroupBy(r => r.AccountId)
            .Select(g => new CategoryAmountDto(
                g.Key.Value, accounts[g.Key].Name, accounts[g.Key].Purpose.ToString(), g.Sum(r => r.Amount)))
            .Where(c => c.Amount != 0)
            .OrderByDescending(c => c.Amount)
            .ToList();

        var walletBalances = rows
            .Where(r => accounts[r.AccountId].IsWallet)
            .GroupBy(r => r.AccountId)
            .Select(g => new ReportWalletDto(accounts[g.Key].ReferenceId!.Value, accounts[g.Key].Name, g.Sum(r => r.Amount)))
            .Where(w => w.Balance != 0 || !accounts.Values.First(a => a.ReferenceId == w.WalletId).IsArchived)
            .OrderByDescending(w => w.Balance)
            .ToList();

        return new ReportDto(
            currency.ToString(),
            currencies.Select(c => c.ToString()).ToList(),
            granularity,
            buckets.Sum(b => b.Income),
            buckets.Sum(b => b.Expenses),
            buckets,
            spending,
            walletBalances);
    }

    private async Task<Currency> MostActiveCurrencyAsync(
        IQueryable<Posting> postings,
        GetReportQuery query,
        IReadOnlyList<Currency> currencies,
        CancellationToken cancellationToken)
    {
        var busiest = await (
                from p in postings
                join t in dbContext.Transactions on p.TransactionId equals t.Id
                where t.OccurredAt >= query.From && t.OccurredAt <= query.To
                group p by p.Currency into g
                orderby g.Count() descending
                select (Currency?)g.Key)
            .FirstOrDefaultAsync(cancellationToken);

        return busiest ?? (currencies.Count > 0 ? currencies[0] : Currency.EUR);
    }
}
