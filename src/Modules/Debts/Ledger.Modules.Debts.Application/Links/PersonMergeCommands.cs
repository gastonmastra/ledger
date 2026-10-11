using System.Text.Json;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Contracts;
using Ledger.Modules.Debts.Domain;
using Ledger.Modules.Transactions.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application.Links;

/// <summary>Internal: a user accepted being linked to a contact; merge the person into the user.</summary>
public sealed record RequestPersonMergeCommand(Guid RequestId, Guid OwnerId, Guid PreviousPersonId, Guid UserId) : ICommand;

/// <summary>Internal: the ledger recorded (or refused) the copies in the user's books.</summary>
public sealed record ApplyOpeningsResultCommand(Guid MergeId, bool Succeeded, string? Reason) : ICommand;

public sealed record ProjectLinkRequestCommand(Guid RequestId, Guid OwnerId, Guid PersonId, Guid TargetUserId) : ICommand;

public sealed record CloseLinkRequestCommand(Guid RequestId) : ICommand;

/// <param name="Kind">Person or Group.</param>
/// <param name="Amount">Positive: owed to the user; negative: the user owes it.</param>
public sealed record LinkPreviewBalanceDto(Guid CounterpartyId, string Name, string Kind, string Currency, decimal Amount);

/// <param name="Operations">How many operations are copied into the user's books (settled ones included).</param>
/// <param name="Balances">What the user will owe or be owed once linked, per person or group and currency.</param>
public sealed record LinkPreviewDto(Guid RequestId, int Operations, IReadOnlyList<LinkPreviewBalanceDto> Balances);

/// <summary>What accepting a link request would put in my books (the target user, or the owner, may ask).</summary>
public sealed record GetLinkPreviewQuery(Guid UserId, Guid RequestId) : IQuery<LinkPreviewDto>;

/// <summary>
/// Linking a contact that is already in use to a registered user: refuses what would make the merge ambiguous,
/// asks the ledger to copy the shared history into the user's books, then moves the person to the user.
/// </summary>
internal sealed class PersonMergeHandlers(
    IDebtsDbContext dbContext,
    IDebtsEventBus eventBus)
    : ICommandHandler<RequestPersonMergeCommand>,
      ICommandHandler<ApplyOpeningsResultCommand>,
      ICommandHandler<ProjectLinkRequestCommand>,
      ICommandHandler<CloseLinkRequestCommand>,
      IQueryHandler<GetLinkPreviewQuery, LinkPreviewDto>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task Handle(RequestPersonMergeCommand command, CancellationToken cancellationToken)
    {
        if (await dbContext.PersonMerges.AnyAsync(m => m.RequestId == command.RequestId, cancellationToken))
            return;

        var person = command.PreviousPersonId;
        var user = command.UserId;

        var refusal = await RefusalAsync(person, user, cancellationToken);

        if (refusal is not null)
        {
            await eventBus.PublishAsync(new PersonMergeFailed(command.RequestId, refusal), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var operations = await CurrentOperationsOf(person, cancellationToken);
        var debts = await DebtsOfAsync(person, cancellationToken);
        var copies = MergeCopies.Build(operations, debts, await PaymentsOfAsync(operations, cancellationToken), person, user);
        var parties = await PartiesAsync(copies, user, cancellationToken);

        var merge = PersonMerge.Start(command.RequestId, person, user, JsonSerializer.Serialize(parties, Json));
        dbContext.PersonMerges.Add(merge);

        if (copies.Count == 0)
        {
            // Nothing to copy (e.g. only zero-sum group expenses): apply right away.
            await ApplyAsync(merge, cancellationToken);
        }
        else
        {
            await eventBus.PublishAsync(
                new LedgerOpeningsRequested(
                    merge.Id,
                    user,
                    copies.Select(c => new LedgerOpeningEntry(
                            c.Operation.Id, c.Operation.OccurredAt, c.Operation.Description, c.Operation.Currency.ToString(), c.Leg))
                        .ToList()),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ApplyOpeningsResultCommand command, CancellationToken cancellationToken)
    {
        var merge = await dbContext.PersonMerges.SingleOrDefaultAsync(m => m.Id == command.MergeId, cancellationToken)
                    ?? throw new NotYetAvailableException($"Person merge '{command.MergeId}' is not stored yet.");

        if (merge.Status != PersonMergeStatus.Pending)
            return;

        if (command.Succeeded)
        {
            await ApplyAsync(merge, cancellationToken);
        }
        else
        {
            var reason = command.Reason ?? "Rejected by the ledger.";
            merge.MarkFailed(reason);
            await eventBus.PublishAsync(new PersonMergeFailed(merge.RequestId, reason), cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ProjectLinkRequestCommand command, CancellationToken cancellationToken)
    {
        if (await dbContext.LinkRequests.AnyAsync(r => r.RequestId == command.RequestId, cancellationToken))
            return;

        dbContext.LinkRequests.Add(DebtLinkRequest.Create(command.RequestId, command.OwnerId, command.PersonId, command.TargetUserId));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(CloseLinkRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await dbContext.LinkRequests.SingleOrDefaultAsync(r => r.RequestId == command.RequestId, cancellationToken);

        if (request is null)
            return;

        dbContext.LinkRequests.Remove(request);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<LinkPreviewDto> Handle(GetLinkPreviewQuery query, CancellationToken cancellationToken)
    {
        var request = await dbContext.LinkRequests.AsNoTracking()
            .SingleOrDefaultAsync(r => r.RequestId == query.RequestId, cancellationToken);

        // The request may still be on its way to this module.
        if (request is null)
            throw new NotYetAvailableException($"Link request '{query.RequestId}' is not available yet.");

        if (request.TargetUserId != query.UserId && request.OwnerId != query.UserId)
            throw new NotFoundException("Link request", query.RequestId);

        var operations = await CurrentOperationsOf(request.PersonId, cancellationToken);
        var copies = MergeCopies.Build(
            operations,
            await DebtsOfAsync(request.PersonId, cancellationToken),
            await PaymentsOfAsync(operations, cancellationToken),
            request.PersonId,
            request.TargetUserId);

        var groupNames = await dbContext.GroupMembers.AsNoTracking()
            .Select(m => new { m.GroupId, m.GroupName })
            .Distinct()
            .ToDictionaryAsync(g => g.GroupId, g => g.GroupName, cancellationToken);

        var counterparties = copies.SelectMany(c => c.Leg.Positions).Select(p => p.CounterpartyId).Distinct().ToList();
        var names = await NamesAsync(counterparties.Where(id => !groupNames.ContainsKey(id)), cancellationToken);

        var balances = copies
            .SelectMany(c => c.Leg.Positions.Select(p => (Copy: c, Position: p)))
            .GroupBy(x => (x.Position.CounterpartyId, Currency: x.Copy.Operation.Currency.ToString()))
            .Select(g =>
            {
                var isGroup = groupNames.TryGetValue(g.Key.CounterpartyId, out var groupName);
                return new LinkPreviewBalanceDto(
                    g.Key.CounterpartyId,
                    isGroup ? groupName! : names.GetValueOrDefault(g.Key.CounterpartyId, "(unknown)"),
                    isGroup ? "Group" : "Person",
                    g.Key.Currency,
                    g.Sum(x => x.Position.Amount));
            })
            .Where(b => b.Amount != 0)
            .OrderBy(b => b.Currency).ThenBy(b => b.Name)
            .ToList();

        return new LinkPreviewDto(request.RequestId, copies.Count, balances);
    }

    /// <summary>The copies are in the user's books: everything that referred to the person now refers to the user.</summary>
    private async Task ApplyAsync(PersonMerge merge, CancellationToken cancellationToken)
    {
        await dbContext.ReplacePersonAsync(merge.PreviousPersonId, merge.UserId, cancellationToken);
        merge.MarkApplied();

        var parties = JsonSerializer.Deserialize<List<DebtParty>>(merge.Parties, Json) ?? [];

        await eventBus.PublishAsync(
            new PersonMerged(merge.RequestId, merge.PreviousPersonId, merge.UserId, parties),
            cancellationToken);
    }

    /// <summary>Why the person cannot be merged right now, or null.</summary>
    private async Task<string?> RefusalAsync(Guid person, Guid user, CancellationToken cancellationToken)
    {
        if (await dbContext.PersonMerges.AnyAsync(
                m => m.PreviousPersonId == person && m.Status == PersonMergeStatus.Pending, cancellationToken))
            return "This contact is already being linked.";

        var operations = await OperationsOf(person)
            .Where(o => o.Status == OperationStatus.Pending || o.Status == OperationStatus.Completed)
            .ToListAsync(cancellationToken);

        if (operations.Any(o => o.Status == OperationStatus.Pending))
            return "An operation with this contact is still being recorded. Try again in a moment.";

        if (operations.Any(o => o.PendingChangeId is not null))
            return "A change of an operation with this contact is waiting for confirmation. Accept the link once it is answered.";

        if (operations.Any(o => o.InvolvedPersonIds.Contains(user)))
            return "You already take part in some of this contact's operations as yourself, so they can't be merged.";

        if (await dbContext.Debts.AnyAsync(
                d => (d.CreditorPersonId == person || d.DebtorPersonId == person)
                     && d.Status == DebtStatus.Open && d.SettlementOperationId != null,
                cancellationToken))
            return "A debt with this contact is being settled. Try again in a moment.";

        var groupsOfPerson = dbContext.GroupMembers.Where(m => m.PersonId == person).Select(m => m.GroupId);

        if (await dbContext.GroupMembers.AnyAsync(m => m.PersonId == user && groupsOfPerson.Contains(m.GroupId), cancellationToken))
            return "You are already a member of a group where this contact is a member, so they can't be merged.";

        return null;
    }

    private async Task<IReadOnlyList<DebtPayment>> PaymentsOfAsync(IReadOnlyList<DebtOperation> operations, CancellationToken cancellationToken)
    {
        var ids = operations.Where(o => o.Kind == OperationKind.Settlement).Select(o => o.Id).ToList();

        return await dbContext.Payments.AsNoTracking()
            .Where(p => ids.Contains(p.SettlementOperationId))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<DebtOperation> OperationsOf(Guid person)
        => dbContext.Operations.Where(o => o.FromPersonId == person
                                          || o.ToPersonId == person
                                          || o.Payers.Any(p => p.PersonId == person)
                                          || o.Shares.Any(s => s.PersonId == person));

    private async Task<List<DebtOperation>> CurrentOperationsOf(Guid person, CancellationToken cancellationToken)
        => await OperationsOf(person)
            .AsNoTracking()
            .Where(o => o.Status == OperationStatus.Completed)
            .ToListAsync(cancellationToken);

    private async Task<List<Debt>> DebtsOfAsync(Guid person, CancellationToken cancellationToken)
        => await dbContext.Debts.AsNoTracking()
            .Where(d => d.CreditorPersonId == person || d.DebtorPersonId == person)
            .ToListAsync(cancellationToken);

    /// <summary>The user and every person (not group) the copies put in their books.</summary>
    private async Task<List<DebtParty>> PartiesAsync(IReadOnlyList<MergeCopy> copies, Guid user, CancellationToken cancellationToken)
    {
        var groupIds = copies.Where(c => c.Operation.GroupId is not null).Select(c => c.Operation.GroupId!.Value).ToHashSet();

        var people = copies
            .SelectMany(c => c.Leg.Positions)
            .Select(p => p.CounterpartyId)
            .Where(id => !groupIds.Contains(id))
            .Append(user)
            .Distinct()
            .ToList();

        var names = await NamesAsync(people, cancellationToken);
        var users = await dbContext.Users.AsNoTracking()
            .Where(u => people.Contains(u.UserId))
            .Select(u => u.UserId)
            .ToListAsync(cancellationToken);

        return people
            .Select(id => new DebtParty(id, names.GetValueOrDefault(id, "(unknown)"), users.Contains(id) ? id : null))
            .ToList();
    }

    /// <summary>Display names of people: registered users by their name, others by the name of any contact for them.</summary>
    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> personIds, CancellationToken cancellationToken)
    {
        var ids = personIds.ToList();

        var names = await dbContext.Contacts.AsNoTracking()
            .Where(c => ids.Contains(c.PersonId) && !c.IsMerged)
            .GroupBy(c => c.PersonId)
            .Select(g => new { PersonId = g.Key, Name = g.Min(c => c.Name)! })
            .ToDictionaryAsync(x => x.PersonId, x => x.Name, cancellationToken);

        foreach (var user in await dbContext.Users.AsNoTracking().Where(u => ids.Contains(u.UserId)).ToListAsync(cancellationToken))
            names[user.UserId] = user.DisplayName;

        return names;
    }
}

/// <summary>While a person is being merged into a user, no new operation or change may involve them.</summary>
internal sealed class MergeGuard(IDebtsDbContext dbContext)
{
    public async Task EnsureNotMergingAsync(IEnumerable<Guid> personIds, CancellationToken cancellationToken)
    {
        var ids = personIds.Distinct().ToList();

        if (await dbContext.PersonMerges.AnyAsync(
                m => m.Status == PersonMergeStatus.Pending && ids.Contains(m.PreviousPersonId), cancellationToken))
            throw new DomainException(
                "contact.link_pending",
                "This contact is being linked to their Ledger account. Try again in a moment.");
    }
}
