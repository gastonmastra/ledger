using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Queries;

/// <param name="Wallets">Sum of the wallet balances.</param>
/// <param name="OwedToMe">What other people owe me.</param>
/// <param name="IOwe">What I owe other people.</param>
/// <param name="Pending">Money paid or received on my behalf that still needs a wallet.</param>
/// <param name="Net">Wallets + OwedToMe − IOwe + Pending.</param>
public sealed record CurrencySummaryDto(
    string Currency,
    decimal Wallets,
    decimal OwedToMe,
    decimal IOwe,
    decimal Pending,
    decimal Net);

public sealed record WalletBalanceDto(Guid WalletId, string Name, string Currency, decimal Balance);

public sealed record CounterpartyBalanceDto(
    Guid CounterpartyId,
    string Name,
    string Kind,
    string Currency,
    decimal OwedToMe,
    decimal IOwe,
    decimal Net);

public sealed record SummaryDto(
    IReadOnlyList<CurrencySummaryDto> Totals,
    IReadOnlyList<WalletBalanceDto> Wallets,
    IReadOnlyList<CounterpartyBalanceDto> Counterparties);

public sealed record GetSummaryQuery(Guid UserId) : IQuery<SummaryDto>;

internal sealed class SummaryQueryHandler(ITransactionsDbContext dbContext)
    : IQueryHandler<GetSummaryQuery, SummaryDto>
{
    private static readonly LedgerAccountPurpose[] DebtPurposes =
    [
        LedgerAccountPurpose.Receivables,
        LedgerAccountPurpose.Payables,
        LedgerAccountPurpose.PendingSettlements
    ];

    public async Task<SummaryDto> Handle(GetSummaryQuery query, CancellationToken cancellationToken)
    {
        var accounts = await dbContext.LedgerAccounts
            .AsNoTracking()
            .Where(a => a.OwnerId == query.UserId)
            .ToListAsync(cancellationToken);

        var wallets = accounts
            .Where(a => a.IsWallet && (!a.IsArchived || a.Balance != 0))
            .OrderBy(a => a.Currency).ThenBy(a => a.Name)
            .Select(a => new WalletBalanceDto(a.ReferenceId!.Value, a.Name, a.Currency!.Value.ToString(), a.Balance))
            .ToList();

        var debtAccounts = accounts
            .Where(a => DebtPurposes.Contains(a.Purpose))
            .ToDictionary(a => a.Id, a => a.Purpose);
        var debtAccountIds = debtAccounts.Keys.ToList();

        var positions = (await dbContext.Postings
                .AsNoTracking()
                .Where(p => debtAccountIds.Contains(p.AccountId))
                .GroupBy(p => new { p.AccountId, p.Currency, p.CounterpartyId })
                .Select(g => new { g.Key.AccountId, g.Key.Currency, g.Key.CounterpartyId, Amount = g.Sum(p => p.Amount) })
                .ToListAsync(cancellationToken))
            .Select(x => new
            {
                Purpose = debtAccounts[x.AccountId],
                Currency = x.Currency.ToString(),
                x.CounterpartyId,
                x.Amount
            })
            .ToList();

        var counterpartyNames = await dbContext.Counterparties
            .AsNoTracking()
            .Where(c => c.OwnerId == query.UserId)
            .ToDictionaryAsync(c => c.PersonId, c => (c.Name, Kind: c.Kind.ToString()), cancellationToken);

        var counterparties = positions
            .Where(p => p.Purpose != LedgerAccountPurpose.PendingSettlements && p.CounterpartyId is not null)
            .GroupBy(p => (Id: p.CounterpartyId!.Value, p.Currency))
            .Select(g =>
            {
                var owedToMe = g.Where(p => p.Purpose == LedgerAccountPurpose.Receivables).Sum(p => p.Amount);
                var iOwe = -g.Where(p => p.Purpose == LedgerAccountPurpose.Payables).Sum(p => p.Amount);
                var (name, kind) = counterpartyNames.GetValueOrDefault(g.Key.Id, ("(unknown)", "Contact"));

                return new CounterpartyBalanceDto(g.Key.Id, name, kind, g.Key.Currency, owedToMe, iOwe, owedToMe - iOwe);
            })
            .Where(c => c.OwedToMe != 0 || c.IOwe != 0)
            .OrderBy(c => c.Currency).ThenBy(c => c.Name)
            .ToList();

        var currencies = wallets.Select(w => w.Currency)
            .Concat(positions.Select(p => p.Currency))
            .Distinct()
            .Order();

        var totals = currencies
            .Select(currency =>
            {
                var walletTotal = wallets.Where(w => w.Currency == currency).Sum(w => w.Balance);
                var owedToMe = positions
                    .Where(p => p.Currency == currency && p.Purpose == LedgerAccountPurpose.Receivables)
                    .Sum(p => p.Amount);
                var iOwe = -positions
                    .Where(p => p.Currency == currency && p.Purpose == LedgerAccountPurpose.Payables)
                    .Sum(p => p.Amount);
                var pending = positions
                    .Where(p => p.Currency == currency && p.Purpose == LedgerAccountPurpose.PendingSettlements)
                    .Sum(p => p.Amount);

                return new CurrencySummaryDto(
                    currency, walletTotal, owedToMe, iOwe, pending, walletTotal + owedToMe - iOwe + pending);
            })
            .ToList();

        return new SummaryDto(totals, wallets, counterparties);
    }
}
