using Ledger.Modules.Accounts.Infrastructure.Persistence;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Accounts.Application.System;
using Ledger.Modules.Identity.Contracts;
using Ledger.Modules.Wallets.Contracts;
using MassTransit;
using MediatR;

namespace Ledger.Modules.Accounts.Infrastructure.Consumers;

internal sealed class UserRegisteredConsumer(ISender sender, AccountsDbContext dbContext)
    : ModuleConsumer<UserRegistered, AccountsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<UserRegistered> context)
        => sender.Send(new ProvisionUserAccountsCommand(context.Message.UserId), context.CancellationToken);
}

internal sealed class WalletCreatedConsumer(ISender sender, AccountsDbContext dbContext)
    : ModuleConsumer<WalletCreated, AccountsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<WalletCreated> context)
    {
        var wallet = context.Message;

        return sender.Send(
            new OpenWalletAccountCommand(
                wallet.WalletId, wallet.UserId, wallet.Name, wallet.Currency, wallet.OpeningBalance, wallet.OpeningBalanceAt),
            context.CancellationToken);
    }
}

internal sealed class WalletRenamedConsumer(ISender sender, AccountsDbContext dbContext)
    : ModuleConsumer<WalletRenamed, AccountsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<WalletRenamed> context)
        => sender.Send(
            new RenameWalletAccountCommand(context.Message.WalletId, context.Message.Name),
            context.CancellationToken);
}

internal sealed class WalletArchivedConsumer(ISender sender, AccountsDbContext dbContext)
    : ModuleConsumer<WalletArchived, AccountsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<WalletArchived> context)
        => sender.Send(new ArchiveWalletAccountCommand(context.Message.WalletId), context.CancellationToken);
}

internal sealed class WalletDeletedConsumer(ISender sender, AccountsDbContext dbContext)
    : ModuleConsumer<WalletDeleted, AccountsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<WalletDeleted> context)
        => sender.Send(new DeleteWalletAccountCommand(context.Message.WalletId), context.CancellationToken);
}
