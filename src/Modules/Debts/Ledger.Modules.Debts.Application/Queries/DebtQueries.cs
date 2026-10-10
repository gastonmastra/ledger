using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Application.Operations;
using Ledger.Modules.Debts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application.Queries;

public enum DebtDirection
{
    OwedToMe,
    IOwe
}

public sealed record DebtDto(
    Guid Id,
    Guid OperationId,
    Guid? GroupId,
    string? GroupName,
    Guid CounterpartyPersonId,
    string CounterpartyName,
    DebtDirection Direction,
    decimal Amount,
    string Currency,
    DebtStatus Status,
    bool IsSettling,
    string? Description,
    DateTimeOffset CreatedAt,
    bool PendingChange = false,
    bool Disputed = false);

public sealed record CounterpartyDebtsDto(
    Guid PersonId,
    string Name,
    string Currency,
    decimal OwedToMe,
    decimal IOwe,
    decimal Net);

/// <param name="ByContact">Open debts outside groups, netted per person and currency.</param>
public sealed record MyDebtsDto(IReadOnlyList<DebtDto> Debts, IReadOnlyList<CounterpartyDebtsDto> ByContact);

public sealed record GetMyDebtsQuery(Guid UserId, bool IncludeSettled) : IQuery<MyDebtsDto>;

public sealed record MemberBalanceDto(Guid PersonId, string Name, string Currency, decimal Balance);

public sealed record SuggestedPaymentDto(
    Guid FromPersonId,
    string FromName,
    Guid ToPersonId,
    string ToName,
    decimal Amount,
    string Currency);

/// <param name="Balances">Positive: the group owes the member. Negative: the member owes the group.</param>
/// <param name="SuggestedPayments">Simplified: the fewest payments that settle the group.</param>
/// <param name="Debts">Raw who-owes-whom pairs from the group expenses.</param>
public sealed record GroupBalancesDto(
    Guid GroupId,
    string GroupName,
    IReadOnlyList<MemberBalanceDto> Balances,
    IReadOnlyList<SuggestedPaymentDto> SuggestedPayments,
    IReadOnlyList<DebtDto> Debts);

public sealed record GetGroupBalancesQuery(Guid UserId, Guid GroupId) : IQuery<GroupBalancesDto>;

internal sealed class DebtQueryHandlers(
    IDebtsDbContext dbContext,
    PeopleDirectory people,
    GroupBalances groupBalances)
    : IQueryHandler<GetMyDebtsQuery, MyDebtsDto>,
      IQueryHandler<GetGroupBalancesQuery, GroupBalancesDto>
{
    public async Task<MyDebtsDto> Handle(GetMyDebtsQuery query, CancellationToken cancellationToken)
    {
        var me = query.UserId;
        var statuses = query.IncludeSettled
            ? new[] { DebtStatus.Pending, DebtStatus.Open, DebtStatus.Settled }
            : new[] { DebtStatus.Pending, DebtStatus.Open };

        var debts = await dbContext.Debts.AsNoTracking()
            .Where(d => (d.CreditorPersonId == me || d.DebtorPersonId == me) && statuses.Contains(d.Status))
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        var names = await people.NamesAsync(me, cancellationToken);
        var descriptions = await DescriptionsAsync(debts, cancellationToken);
        var dtos = debts.Select(d => ToDto(d, me, names, descriptions)).ToList();

        var byContact = dtos
            .Where(d => d.GroupId is null && d.Status == DebtStatus.Open)
            .GroupBy(d => (d.CounterpartyPersonId, d.CounterpartyName, d.Currency))
            .Select(g =>
            {
                var owedToMe = g.Where(d => d.Direction == DebtDirection.OwedToMe).Sum(d => d.Amount);
                var iOwe = g.Where(d => d.Direction == DebtDirection.IOwe).Sum(d => d.Amount);

                return new CounterpartyDebtsDto(g.Key.CounterpartyPersonId, g.Key.CounterpartyName, g.Key.Currency,
                    owedToMe, iOwe, owedToMe - iOwe);
            })
            .OrderBy(c => c.Name).ThenBy(c => c.Currency)
            .ToList();

        return new MyDebtsDto(dtos, byContact);
    }

    public async Task<GroupBalancesDto> Handle(GetGroupBalancesQuery query, CancellationToken cancellationToken)
    {
        var (groupName, members) = await people.GroupAsync(query.UserId, query.GroupId, cancellationToken);
        var balances = await groupBalances.ComputeAsync(query.GroupId, members.Keys, cancellationToken);

        string Name(Guid personId) => personId == query.UserId
            ? "Me"
            : members.TryGetValue(personId, out var p) ? p.Name : "(former member)";

        var memberBalances = balances
            .OrderBy(b => b.Key)
            .SelectMany(b => b.Value.Select(m => new MemberBalanceDto(m.Key, Name(m.Key), b.Key.ToString(), m.Value)))
            .ToList();

        var suggestions = balances
            .OrderBy(b => b.Key)
            .SelectMany(b => SplitMath.Simplify(b.Value)
                .Select(p => new SuggestedPaymentDto(
                    p.FromPersonId, Name(p.FromPersonId), p.ToPersonId, Name(p.ToPersonId), p.Amount, b.Key.ToString())))
            .ToList();

        var debts = await dbContext.Debts.AsNoTracking()
            .Where(d => d.GroupId == query.GroupId && d.Status == DebtStatus.Open)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        var names = members.ToDictionary(m => m.Key, m => Name(m.Key));
        names[query.GroupId] = groupName;
        var descriptions = await DescriptionsAsync(debts, cancellationToken);

        // Raw pairs are shown from the creditor's side.
        var debtDtos = debts
            .Select(d => ToDto(d, d.DebtorPersonId, names, descriptions) with { CounterpartyName = $"{Name(d.DebtorPersonId)} owes {Name(d.CreditorPersonId)}" })
            .ToList();

        return new GroupBalancesDto(query.GroupId, groupName, memberBalances, suggestions, debtDtos);
    }

    private async Task<Dictionary<Guid, OperationInfo>> DescriptionsAsync(IReadOnlyList<Debt> debts, CancellationToken cancellationToken)
    {
        var operationIds = debts.Select(d => d.OperationId).Distinct().ToList();

        return await dbContext.Operations.AsNoTracking()
            .Where(o => operationIds.Contains(o.Id))
            .ToDictionaryAsync(
                o => o.Id,
                o => new OperationInfo(o.Description, o.PendingChangeId != null, o.IsDisputed),
                cancellationToken);
    }

    private sealed record OperationInfo(string? Description, bool PendingChange, bool Disputed);

    private static DebtDto ToDto(
        Debt debt,
        Guid me,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<Guid, OperationInfo> operations)
    {
        var owedToMe = debt.CreditorPersonId == me;
        var counterparty = owedToMe ? debt.DebtorPersonId : debt.CreditorPersonId;

        return new DebtDto(
            debt.Id,
            debt.OperationId,
            debt.GroupId,
            debt.GroupId is { } g ? names.GetValueOrDefault(g) : null,
            counterparty,
            names.GetValueOrDefault(counterparty) ?? "(unknown)",
            owedToMe ? DebtDirection.OwedToMe : DebtDirection.IOwe,
            debt.Amount,
            debt.Currency.ToString(),
            debt.Status,
            debt.IsSettling,
            operations.GetValueOrDefault(debt.OperationId)?.Description,
            debt.CreatedAt,
            operations.GetValueOrDefault(debt.OperationId)?.PendingChange ?? false,
            operations.GetValueOrDefault(debt.OperationId)?.Disputed ?? false);
    }
}
