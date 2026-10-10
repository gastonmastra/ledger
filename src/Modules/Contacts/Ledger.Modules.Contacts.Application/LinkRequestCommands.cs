using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Contacts.Contracts;
using Ledger.Modules.Contacts.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Contacts.Application;

public enum LinkRequestDirection
{
    /// <summary>Somebody wants to link one of their contacts to me.</summary>
    Incoming,
    /// <summary>I asked a user to accept being linked to one of my contacts.</summary>
    Outgoing
}

/// <param name="ContactName">The name the owner gave the contact.</param>
/// <param name="OwnerName">Who sent it.</param>
/// <param name="TargetName">The user who has to answer.</param>
public sealed record LinkRequestDto(
    Guid Id,
    LinkRequestDirection Direction,
    Guid ContactId,
    string ContactName,
    Guid OwnerId,
    string OwnerName,
    Guid TargetUserId,
    string TargetName,
    LinkRequestStatus Status,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AnsweredAt);

/// <param name="IncludeClosed">Also answered, cancelled and failed requests (most recent first).</param>
public sealed record GetLinkRequestsQuery(Guid UserId, bool IncludeClosed = false) : IQuery<IReadOnlyList<LinkRequestDto>>;

public sealed record AcceptLinkRequestCommand(Guid UserId, Guid RequestId) : ICommand<LinkRequestDto>;

public sealed record RejectLinkRequestCommand(Guid UserId, Guid RequestId) : ICommand<LinkRequestDto>;

public sealed record CancelLinkRequestCommand(Guid UserId, Guid RequestId) : ICommand<LinkRequestDto>;

/// <summary>Internal: the person was merged into the user everywhere; the contacts follow.</summary>
public sealed record ApplyPersonMergeCommand(
    Guid RequestId,
    Guid PreviousPersonId,
    Guid UserId,
    IReadOnlyList<PersonReference> Parties) : ICommand;

/// <summary>Internal: the merge could not be applied; nothing changed.</summary>
public sealed record FailPersonMergeCommand(Guid RequestId, string Reason) : ICommand;

internal sealed class LinkRequestHandlers(
    IContactsDbContext dbContext,
    IContactsEventBus eventBus,
    ISender sender)
    : IQueryHandler<GetLinkRequestsQuery, IReadOnlyList<LinkRequestDto>>,
      ICommandHandler<AcceptLinkRequestCommand, LinkRequestDto>,
      ICommandHandler<RejectLinkRequestCommand, LinkRequestDto>,
      ICommandHandler<CancelLinkRequestCommand, LinkRequestDto>,
      ICommandHandler<ApplyPersonMergeCommand>,
      ICommandHandler<FailPersonMergeCommand>
{
    private const int MaxClosed = 50;

    public async Task<IReadOnlyList<LinkRequestDto>> Handle(GetLinkRequestsQuery query, CancellationToken cancellationToken)
    {
        var requests = await dbContext.LinkRequests.AsNoTracking()
            .Where(r => (r.OwnerId == query.UserId || r.TargetUserId == query.UserId)
                        && (query.IncludeClosed
                            || r.Status == LinkRequestStatus.Pending
                            || r.Status == LinkRequestStatus.Applying))
            .OrderByDescending(r => r.CreatedAt)
            .Take(MaxClosed)
            .ToListAsync(cancellationToken);

        var names = await NamesAsync(requests.SelectMany(r => new[] { r.OwnerId, r.TargetUserId }), cancellationToken);

        return requests.Select(r => ToDto(r, query.UserId, names)).ToList();
    }

    public async Task<LinkRequestDto> Handle(AcceptLinkRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await GetAsync(command.RequestId, command.UserId, cancellationToken);

        request.Accept(command.UserId);

        await eventBus.PublishAsync(
            new PersonMergeRequested(request.Id, request.OwnerId, request.ContactId, request.PersonId, request.TargetUserId),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await DtoAsync(request, command.UserId, cancellationToken);
    }

    public async Task<LinkRequestDto> Handle(RejectLinkRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await GetAsync(command.RequestId, command.UserId, cancellationToken);

        request.Reject(command.UserId);
        await CloseAsync(request, cancellationToken);

        return await DtoAsync(request, command.UserId, cancellationToken);
    }

    public async Task<LinkRequestDto> Handle(CancelLinkRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await GetAsync(command.RequestId, command.UserId, cancellationToken);

        request.Cancel(command.UserId);
        await CloseAsync(request, cancellationToken);

        return await DtoAsync(request, command.UserId, cancellationToken);
    }

    public async Task Handle(ApplyPersonMergeCommand command, CancellationToken cancellationToken)
    {
        var request = await dbContext.LinkRequests.SingleAsync(r => r.Id == command.RequestId, cancellationToken);

        if (!request.MarkApplied())
            return;

        var user = await dbContext.RegisteredUsers.AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserId == command.UserId, cancellationToken);

        // Every contact of that person becomes the user: the owner's, and the ones other users got mirrored.
        var contacts = await dbContext.Contacts
            .Where(c => c.PersonId == command.PreviousPersonId && c.MergedIntoContactId == null)
            .ToListAsync(cancellationToken);

        var owners = contacts.Select(c => c.OwnerId).ToList();
        var survivors = await dbContext.Contacts
            .Where(c => owners.Contains(c.OwnerId) && c.PersonId == command.UserId && c.MergedIntoContactId == null)
            .ToDictionaryAsync(c => c.OwnerId, cancellationToken);

        // The user never gets a contact for themselves.
        foreach (var contact in contacts.Where(c => c.OwnerId != command.UserId))
        {
            if (survivors.TryGetValue(contact.OwnerId, out var survivor))
            {
                contact.MergeInto(survivor);
                await PublishLinkedAsync(contact, command.PreviousPersonId, command.UserId, survivor.Id, cancellationToken);
            }
            else
            {
                contact.CompleteMerge(command.UserId, contact.OwnerId == request.OwnerId ? user?.Email : null);
                await PublishLinkedAsync(contact, command.PreviousPersonId, command.UserId, null, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // The user and the people they now share debts with see each other in their contacts.
        await sender.Send(new EnsureMutualContactsCommand(command.Parties), cancellationToken);
    }

    public async Task Handle(FailPersonMergeCommand command, CancellationToken cancellationToken)
    {
        var request = await dbContext.LinkRequests.SingleAsync(r => r.Id == command.RequestId, cancellationToken);

        if (!request.MarkFailed(command.Reason))
            return;

        await CloseAsync(request, cancellationToken);
    }

    private async Task CloseAsync(ContactLinkRequest request, CancellationToken cancellationToken)
    {
        var contact = await dbContext.Contacts.SingleOrDefaultAsync(c => c.Id == request.ContactId, cancellationToken);
        contact?.ClearLinkRequest(request.Id);

        await eventBus.PublishAsync(new ContactLinkRequestClosed(request.Id), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task PublishLinkedAsync(
        Contact contact,
        Guid previousPersonId,
        Guid userId,
        Guid? mergedInto,
        CancellationToken cancellationToken)
        => eventBus.PublishAsync(
            new ContactLinked(contact.Id, contact.OwnerId, previousPersonId, userId, userId, contact.Name, mergedInto),
            cancellationToken);

    private async Task<ContactLinkRequest> GetAsync(Guid requestId, Guid userId, CancellationToken cancellationToken)
    {
        var request = await dbContext.LinkRequests.SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request is null || (request.OwnerId != userId && request.TargetUserId != userId))
            throw new NotFoundException("Link request", requestId);

        return request;
    }

    private async Task<LinkRequestDto> DtoAsync(ContactLinkRequest request, Guid userId, CancellationToken cancellationToken)
        => ToDto(request, userId, await NamesAsync([request.OwnerId, request.TargetUserId], cancellationToken));

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();

        return await dbContext.RegisteredUsers.AsNoTracking()
            .Where(u => ids.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.DisplayName, cancellationToken);
    }

    private static LinkRequestDto ToDto(ContactLinkRequest request, Guid userId, IReadOnlyDictionary<Guid, string> names)
        => new(
            request.Id,
            request.TargetUserId == userId ? LinkRequestDirection.Incoming : LinkRequestDirection.Outgoing,
            request.ContactId,
            request.ContactName,
            request.OwnerId,
            names.GetValueOrDefault(request.OwnerId, "(unknown)"),
            request.TargetUserId,
            names.GetValueOrDefault(request.TargetUserId, "(unknown)"),
            request.Status,
            request.FailureReason,
            request.CreatedAt,
            request.AnsweredAt);
}
