using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application.Projections;

public sealed record ProjectContactCommand(Guid ContactId, Guid OwnerId, Guid PersonId, string Name, Guid? LinkedUserId) : ICommand;

public sealed record ProjectContactLinkCommand(Guid ContactId, Guid PersonId, Guid LinkedUserId) : ICommand;

public sealed record ProjectUserCommand(Guid UserId, string DisplayName) : ICommand;

public sealed record ProjectGroupMember(Guid PersonId, string DisplayName, Guid? UserId);

public sealed record ProjectGroupMembersCommand(Guid GroupId, string GroupName, IReadOnlyList<ProjectGroupMember> Members) : ICommand;

public sealed record RemoveGroupMemberProjectionCommand(Guid GroupId, Guid PersonId) : ICommand;

public sealed record ProjectWalletCommand(Guid WalletId, Guid UserId, string Currency) : ICommand;

public sealed record RemoveWalletProjectionCommand(Guid WalletId) : ICommand;

internal sealed class ProjectionHandlers(IDebtsDbContext dbContext)
    : ICommandHandler<ProjectContactCommand>,
      ICommandHandler<ProjectContactLinkCommand>,
      ICommandHandler<ProjectUserCommand>,
      ICommandHandler<ProjectGroupMembersCommand>,
      ICommandHandler<RemoveGroupMemberProjectionCommand>,
      ICommandHandler<ProjectWalletCommand>,
      ICommandHandler<RemoveWalletProjectionCommand>
{
    public async Task Handle(ProjectContactCommand command, CancellationToken cancellationToken)
    {
        if (await dbContext.Contacts.AnyAsync(x => x.ContactId == command.ContactId, cancellationToken))
            return;

        dbContext.Contacts.Add(DebtContact.Create(
            command.ContactId, command.OwnerId, command.PersonId, command.Name, command.LinkedUserId));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ProjectContactLinkCommand command, CancellationToken cancellationToken)
    {
        var contact = await dbContext.Contacts.SingleOrDefaultAsync(x => x.ContactId == command.ContactId, cancellationToken)
            ?? throw new BuildingBlocks.Application.Exceptions.NotYetAvailableException(
                $"Contact '{command.ContactId}' is not projected yet.");

        contact.Link(command.PersonId, command.LinkedUserId);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ProjectUserCommand command, CancellationToken cancellationToken)
    {
        if (await dbContext.Users.AnyAsync(x => x.UserId == command.UserId, cancellationToken))
            return;

        dbContext.Users.Add(DebtUser.Create(command.UserId, command.DisplayName));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ProjectGroupMembersCommand command, CancellationToken cancellationToken)
    {
        var existing = await dbContext.GroupMembers
            .Where(m => m.GroupId == command.GroupId)
            .Select(m => m.PersonId)
            .ToListAsync(cancellationToken);

        foreach (var member in command.Members.Where(m => !existing.Contains(m.PersonId)))
        {
            dbContext.GroupMembers.Add(DebtGroupMember.Create(
                command.GroupId, command.GroupName, member.PersonId, member.DisplayName, member.UserId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(RemoveGroupMemberProjectionCommand command, CancellationToken cancellationToken)
    {
        var member = await dbContext.GroupMembers.SingleOrDefaultAsync(
            m => m.GroupId == command.GroupId && m.PersonId == command.PersonId, cancellationToken);

        if (member is null)
            return;

        dbContext.GroupMembers.Remove(member);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ProjectWalletCommand command, CancellationToken cancellationToken)
    {
        if (await dbContext.Wallets.AnyAsync(x => x.WalletId == command.WalletId, cancellationToken))
            return;

        dbContext.Wallets.Add(DebtWallet.Create(command.WalletId, command.UserId, Currencies.Parse(command.Currency)));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(RemoveWalletProjectionCommand command, CancellationToken cancellationToken)
    {
        var wallet = await dbContext.Wallets.SingleOrDefaultAsync(x => x.WalletId == command.WalletId, cancellationToken);

        if (wallet is null)
            return;

        dbContext.Wallets.Remove(wallet);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
