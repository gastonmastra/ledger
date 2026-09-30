using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.Modules.Groups.Contracts;
using Ledger.Modules.Groups.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Groups.Application;

public sealed record GroupMemberDto(Guid PersonId, string DisplayName, Guid? UserId, bool IsMe, bool HasActivity);

public sealed record GroupDto(Guid Id, string Name, Guid CreatedBy, DateTimeOffset CreatedAt, IReadOnlyList<GroupMemberDto> Members);

public sealed record CreateGroupCommand(Guid UserId, string Name, IReadOnlyList<Guid> ContactIds) : ICommand<GroupDto>;

public sealed record AddGroupMemberCommand(Guid UserId, Guid GroupId, Guid ContactId) : ICommand<GroupDto>;

public sealed record RemoveGroupMemberCommand(Guid UserId, Guid GroupId, Guid PersonId) : ICommand;

public sealed record GetGroupsQuery(Guid UserId) : IQuery<IReadOnlyList<GroupDto>>;

public sealed record GetGroupQuery(Guid UserId, Guid GroupId) : IQuery<GroupDto>;

public sealed record ProjectContactCommand(Guid ContactId, Guid OwnerId, Guid PersonId, string Name, Guid? LinkedUserId) : ICommand;

public sealed record ProjectContactLinkCommand(Guid ContactId, Guid PersonId, Guid LinkedUserId) : ICommand;

public sealed record ProjectUserCommand(Guid UserId, string DisplayName) : ICommand;

public sealed record MarkGroupActivityCommand(Guid GroupId, IReadOnlyList<Guid> PersonIds) : ICommand;

internal sealed class CreateGroupValidator : AbstractValidator<CreateGroupCommand>
{
    public CreateGroupValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Group.NameMaxLength);
    }
}

internal sealed class GroupHandlers(
    IGroupsDbContext dbContext,
    IGroupsEventBus eventBus)
    : ICommandHandler<CreateGroupCommand, GroupDto>,
      ICommandHandler<AddGroupMemberCommand, GroupDto>,
      ICommandHandler<RemoveGroupMemberCommand>,
      ICommandHandler<ProjectContactCommand>,
      ICommandHandler<ProjectContactLinkCommand>,
      ICommandHandler<ProjectUserCommand>,
      ICommandHandler<MarkGroupActivityCommand>,
      IQueryHandler<GetGroupsQuery, IReadOnlyList<GroupDto>>,
      IQueryHandler<GetGroupQuery, GroupDto>
{
    public async Task<GroupDto> Handle(CreateGroupCommand command, CancellationToken cancellationToken)
    {
        var creator = await dbContext.KnownUsers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == command.UserId, cancellationToken)
            ?? throw new NotYetAvailableException("Your user is still being set up. Try again in a moment.");

        var group = Group.Create(command.Name, command.UserId, creator.DisplayName);

        foreach (var contactId in command.ContactIds.Distinct())
        {
            var contact = await GetOwnContactAsync(command.UserId, contactId, cancellationToken);
            group.AddMember(contact.PersonId, contact.Name, contact.LinkedUserId);
        }

        dbContext.Groups.Add(group);

        await eventBus.PublishAsync(
            new GroupCreated(group.Id, group.Name, group.CreatedBy, group.Members.Select(ToInfo).ToList()),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(group, command.UserId);
    }

    public async Task<GroupDto> Handle(AddGroupMemberCommand command, CancellationToken cancellationToken)
    {
        var group = await GetVisibleAsync(command.UserId, command.GroupId, cancellationToken);
        var contact = await GetOwnContactAsync(command.UserId, command.ContactId, cancellationToken);

        var member = group.AddMember(contact.PersonId, contact.Name, contact.LinkedUserId);

        await eventBus.PublishAsync(
            new GroupMemberAdded(group.Id, group.Name, ToInfo(member), group.Members.Select(ToInfo).ToList()),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(group, command.UserId);
    }

    public async Task Handle(RemoveGroupMemberCommand command, CancellationToken cancellationToken)
    {
        var group = await GetVisibleAsync(command.UserId, command.GroupId, cancellationToken);

        group.RemoveMember(command.PersonId);

        await eventBus.PublishAsync(new GroupMemberRemoved(group.Id, command.PersonId), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ProjectContactCommand command, CancellationToken cancellationToken)
    {
        if (await dbContext.KnownContacts.AnyAsync(x => x.ContactId == command.ContactId, cancellationToken))
            return;

        dbContext.KnownContacts.Add(KnownContact.Create(
            command.ContactId, command.OwnerId, command.PersonId, command.Name, command.LinkedUserId));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ProjectContactLinkCommand command, CancellationToken cancellationToken)
    {
        var contact = await dbContext.KnownContacts.SingleOrDefaultAsync(x => x.ContactId == command.ContactId, cancellationToken)
            ?? throw new NotYetAvailableException($"Contact '{command.ContactId}' is not projected yet.");

        contact.Link(command.PersonId, command.LinkedUserId);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ProjectUserCommand command, CancellationToken cancellationToken)
    {
        if (await dbContext.KnownUsers.AnyAsync(x => x.UserId == command.UserId, cancellationToken))
            return;

        dbContext.KnownUsers.Add(KnownUser.Create(command.UserId, command.DisplayName));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(MarkGroupActivityCommand command, CancellationToken cancellationToken)
    {
        var group = await dbContext.Groups
            .Include(g => g.Members)
            .SingleOrDefaultAsync(g => g.Id == command.GroupId, cancellationToken);

        if (group is null)
            return;

        group.MarkActivity(command.PersonIds);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GroupDto>> Handle(GetGroupsQuery query, CancellationToken cancellationToken)
    {
        var groups = await dbContext.Groups
            .AsNoTracking()
            .Include(g => g.Members)
            .Where(g => g.Members.Any(m => m.UserId == query.UserId))
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);

        return groups.Select(g => ToDto(g, query.UserId)).ToList();
    }

    public async Task<GroupDto> Handle(GetGroupQuery query, CancellationToken cancellationToken)
        => ToDto(await GetVisibleAsync(query.UserId, query.GroupId, cancellationToken), query.UserId);

    private async Task<Group> GetVisibleAsync(Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await dbContext.Groups
            .Include(g => g.Members)
            .SingleOrDefaultAsync(g => g.Id == groupId, cancellationToken);

        if (group is null || !group.IsVisibleTo(userId))
            throw new NotFoundException("Group", groupId);

        return group;
    }

    private async Task<KnownContact> GetOwnContactAsync(Guid userId, Guid contactId, CancellationToken cancellationToken)
        => await dbContext.KnownContacts.AsNoTracking()
               .SingleOrDefaultAsync(x => x.ContactId == contactId && x.OwnerId == userId, cancellationToken)
           ?? throw new NotFoundException("Contact", contactId);

    private static GroupMemberInfo ToInfo(GroupMember member)
        => new(member.PersonId, member.DisplayName, member.UserId);

    private static GroupDto ToDto(Group group, Guid userId)
        => new(
            group.Id,
            group.Name,
            group.CreatedBy,
            group.CreatedAt,
            group.Members
                .OrderBy(m => m.AddedAt)
                .Select(m => new GroupMemberDto(m.PersonId, m.DisplayName, m.UserId, m.UserId == userId, m.HasActivity))
                .ToList());
}
