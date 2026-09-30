using Ledger.Modules.Transactions.Infrastructure.Persistence;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Accounts.Contracts;
using Ledger.Modules.Contacts.Contracts;
using Ledger.Modules.Groups.Contracts;
using Ledger.Modules.Transactions.Application;
using Ledger.Modules.Transactions.Application.Ledger;
using Ledger.Modules.Transactions.Application.Projections;
using Ledger.Modules.Transactions.Contracts;
using Ledger.Modules.Transactions.Domain;
using MassTransit;
using MediatR;

namespace Ledger.Modules.Transactions.Infrastructure.Consumers;

internal sealed class AccountCreatedConsumer(ISender sender, TransactionsDbContext dbContext)
    : ModuleConsumer<AccountCreated, TransactionsDbContext>(dbContext)
{
    protected override async Task Handle(ConsumeContext<AccountCreated> context)
    {
        var account = context.Message;

        await sender.Send(
            new ProjectAccountCommand(
                account.AccountId,
                account.OwnerId,
                account.Name,
                account.Type,
                account.Purpose,
                account.Currency,
                account.ReferenceId,
                account.Purpose == AccountPurposes.Wallet ? account.OpeningBalance : 0),
            context.CancellationToken);

        if (account.Purpose == AccountPurposes.Wallet && account.OpeningBalance > 0)
        {
            await sender.Send(
                new PostOpeningBalanceCommand(
                    account.AccountId, account.OpeningBalance, account.OpeningBalanceAt ?? account.CreatedAt),
                context.CancellationToken);
        }
    }
}

internal sealed class AccountRenamedConsumer(ISender sender, TransactionsDbContext dbContext)
    : ModuleConsumer<AccountRenamed, TransactionsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<AccountRenamed> context)
        => sender.Send(
            new RenameAccountProjectionCommand(context.Message.AccountId, context.Message.Name),
            context.CancellationToken);
}

internal sealed class AccountArchivedConsumer(ISender sender, TransactionsDbContext dbContext)
    : ModuleConsumer<AccountArchived, TransactionsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<AccountArchived> context)
        => sender.Send(new ArchiveAccountProjectionCommand(context.Message.AccountId), context.CancellationToken);
}

internal sealed class AccountDeletedConsumer(ISender sender, TransactionsDbContext dbContext)
    : ModuleConsumer<AccountDeleted, TransactionsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<AccountDeleted> context)
        => sender.Send(new DeleteAccountProjectionCommand(context.Message.AccountId), context.CancellationToken);
}

internal sealed class ContactCreatedConsumer(ISender sender, TransactionsDbContext dbContext)
    : ModuleConsumer<ContactCreated, TransactionsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<ContactCreated> context)
    {
        var contact = context.Message;

        return sender.Send(
            new ProjectCounterpartyCommand(
                contact.OwnerId, contact.PersonId, CounterpartyKind.Contact, contact.ContactId, contact.Name),
            context.CancellationToken);
    }
}

internal sealed class ContactLinkedConsumer(ISender sender, TransactionsDbContext dbContext)
    : ModuleConsumer<ContactLinked, TransactionsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<ContactLinked> context)
    {
        var contact = context.Message;

        return sender.Send(
            new ProjectCounterpartyCommand(
                contact.OwnerId,
                contact.PersonId,
                CounterpartyKind.Contact,
                contact.ContactId,
                contact.Name,
                contact.PreviousPersonId),
            context.CancellationToken);
    }
}

/// <summary>Group expenses are owed to/by the group as a whole: each member sees the group as a counterparty.</summary>
internal sealed class GroupCreatedConsumer(ISender sender, TransactionsDbContext dbContext)
    : ModuleConsumer<GroupCreated, TransactionsDbContext>(dbContext)
{
    protected override async Task Handle(ConsumeContext<GroupCreated> context)
    {
        var group = context.Message;

        foreach (var member in group.Members.Where(m => m.UserId is not null))
        {
            await sender.Send(
                new ProjectCounterpartyCommand(
                    member.UserId!.Value, group.GroupId, CounterpartyKind.Group, group.GroupId, group.Name),
                context.CancellationToken);
        }
    }
}

internal sealed class GroupMemberAddedConsumer(ISender sender, TransactionsDbContext dbContext)
    : ModuleConsumer<GroupMemberAdded, TransactionsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<GroupMemberAdded> context)
    {
        var message = context.Message;

        if (message.Member.UserId is not { } userId)
            return Task.CompletedTask;

        return sender.Send(
            new ProjectCounterpartyCommand(
                userId, message.GroupId, CounterpartyKind.Group, message.GroupId, message.GroupName),
            context.CancellationToken);
    }
}

internal sealed class LedgerPostingRequestedConsumer(
    ISender sender,
    ITransactionsEventBus eventBus,
    TransactionsDbContext dbContext)
    : ModuleConsumer<LedgerPostingRequested, TransactionsDbContext>(dbContext)
{
    protected override async Task Handle(ConsumeContext<LedgerPostingRequested> context)
    {
        var result = await sender.Send(new PostDebtOperationCommand(context.Message), context.CancellationToken);

        object reply = result.Succeeded
            ? new LedgerPostingCompleted(context.Message.OperationId)
            : new LedgerPostingRejected(context.Message.OperationId, result.RejectionReason!);

        await eventBus.PublishAsync(reply, context.CancellationToken);
    }
}

internal sealed class LedgerRevisionRequestedConsumer(
    ISender sender,
    ITransactionsEventBus eventBus,
    TransactionsDbContext dbContext)
    : ModuleConsumer<LedgerRevisionRequested, TransactionsDbContext>(dbContext)
{
    protected override async Task Handle(ConsumeContext<LedgerRevisionRequested> context)
    {
        var result = await sender.Send(new ReviseDebtOperationCommand(context.Message), context.CancellationToken);

        object reply = result.Succeeded
            ? new LedgerRevisionCompleted(context.Message.ChangeId)
            : new LedgerRevisionRejected(context.Message.ChangeId, result.RejectionReason!);

        await eventBus.PublishAsync(reply, context.CancellationToken);
    }
}
