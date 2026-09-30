using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Contacts.Contracts;
using Ledger.Modules.Debts.Application.Operations;
using Ledger.Modules.Debts.Application.Projections;
using Ledger.Modules.Groups.Contracts;
using Ledger.Modules.Identity.Contracts;
using Ledger.Modules.Transactions.Contracts;
using Ledger.Modules.Wallets.Contracts;
using MassTransit;
using MediatR;

namespace Ledger.Modules.Debts.Infrastructure;

internal sealed class LedgerPostingCompletedConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<LedgerPostingCompleted, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<LedgerPostingCompleted> context)
        => sender.Send(new ApplyLedgerResultCommand(context.Message.OperationId, true, null), context.CancellationToken);
}

internal sealed class LedgerPostingRejectedConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<LedgerPostingRejected, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<LedgerPostingRejected> context)
        => sender.Send(
            new ApplyLedgerResultCommand(context.Message.OperationId, false, context.Message.Reason),
            context.CancellationToken);
}

internal sealed class LedgerRevisionCompletedConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<LedgerRevisionCompleted, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<LedgerRevisionCompleted> context)
        => sender.Send(new ApplyRevisionResultCommand(context.Message.ChangeId, true, null), context.CancellationToken);
}

internal sealed class LedgerRevisionRejectedConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<LedgerRevisionRejected, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<LedgerRevisionRejected> context)
        => sender.Send(
            new ApplyRevisionResultCommand(context.Message.ChangeId, false, context.Message.Reason),
            context.CancellationToken);
}

internal sealed class BooksLockChangedConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<BooksLockChanged, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<BooksLockChanged> context)
        => sender.Send(new ProjectBooksLockCommand(context.Message.UserId, context.Message.LockedUntil), context.CancellationToken);
}

internal sealed class UserRegisteredConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<UserRegistered, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<UserRegistered> context)
        => sender.Send(new ProjectUserCommand(context.Message.UserId, context.Message.DisplayName), context.CancellationToken);
}

internal sealed class ContactCreatedConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<ContactCreated, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<ContactCreated> context)
    {
        var c = context.Message;
        return sender.Send(new ProjectContactCommand(c.ContactId, c.OwnerId, c.PersonId, c.Name, c.LinkedUserId), context.CancellationToken);
    }
}

internal sealed class ContactLinkedConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<ContactLinked, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<ContactLinked> context)
    {
        var c = context.Message;
        return sender.Send(new ProjectContactLinkCommand(c.ContactId, c.PersonId, c.LinkedUserId), context.CancellationToken);
    }
}

internal sealed class GroupCreatedConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<GroupCreated, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<GroupCreated> context)
        => sender.Send(
            new ProjectGroupMembersCommand(
                context.Message.GroupId,
                context.Message.Name,
                context.Message.Members.Select(m => new ProjectGroupMember(m.PersonId, m.DisplayName, m.UserId)).ToList()),
            context.CancellationToken);
}

internal sealed class GroupMemberAddedConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<GroupMemberAdded, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<GroupMemberAdded> context)
        => sender.Send(
            new ProjectGroupMembersCommand(
                context.Message.GroupId,
                context.Message.GroupName,
                context.Message.Members.Select(m => new ProjectGroupMember(m.PersonId, m.DisplayName, m.UserId)).ToList()),
            context.CancellationToken);
}

internal sealed class GroupMemberRemovedConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<GroupMemberRemoved, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<GroupMemberRemoved> context)
        => sender.Send(
            new RemoveGroupMemberProjectionCommand(context.Message.GroupId, context.Message.PersonId),
            context.CancellationToken);
}

internal sealed class WalletCreatedConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<WalletCreated, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<WalletCreated> context)
        => sender.Send(
            new ProjectWalletCommand(context.Message.WalletId, context.Message.UserId, context.Message.Currency),
            context.CancellationToken);
}

internal sealed class WalletDeletedConsumer(ISender sender, DebtsDbContext dbContext)
    : ModuleConsumer<WalletDeleted, DebtsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<WalletDeleted> context)
        => sender.Send(new RemoveWalletProjectionCommand(context.Message.WalletId), context.CancellationToken);
}
