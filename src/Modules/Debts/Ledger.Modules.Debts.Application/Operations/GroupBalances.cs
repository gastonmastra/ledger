using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application.Operations;

/// <summary>What each member of a group is owed (+) or owes (−), per currency, from completed operations.</summary>
internal sealed class GroupBalances(IDebtsDbContext dbContext)
{
    public async Task<Dictionary<Currency, Dictionary<Guid, decimal>>> ComputeAsync(
        Guid groupId,
        IEnumerable<Guid> memberIds,
        CancellationToken cancellationToken)
    {
        var operations = await dbContext.Operations
            .AsNoTracking()
            .Where(o => o.GroupId == groupId && o.Status == OperationStatus.Completed)
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Currency, Dictionary<Guid, decimal>>();
        var members = memberIds.ToList();

        foreach (var operation in operations)
        {
            if (!result.TryGetValue(operation.Currency, out var balances))
            {
                balances = members.ToDictionary(m => m, _ => 0m);
                result[operation.Currency] = balances;
            }

            foreach (var payer in operation.Payers)
                balances[payer.PersonId] = balances.GetValueOrDefault(payer.PersonId) + payer.Amount;

            foreach (var share in operation.Shares)
                balances[share.PersonId] = balances.GetValueOrDefault(share.PersonId) - share.Amount;

            if (operation.Kind == OperationKind.Settlement && operation.Total > 0)
                balances[operation.ToPersonId!.Value] = balances.GetValueOrDefault(operation.ToPersonId!.Value) - operation.Total;
        }

        return result;
    }
}
