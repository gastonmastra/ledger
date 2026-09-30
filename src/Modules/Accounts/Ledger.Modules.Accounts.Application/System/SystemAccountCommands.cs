using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Accounts.Contracts;
using Ledger.Modules.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Accounts.Application.System;

/// <summary>Creates the system accounts of a new user. Idempotent.</summary>
public sealed record ProvisionUserAccountsCommand(Guid UserId) : ICommand;

/// <summary>Creates the asset account backing a wallet. Idempotent.</summary>
public sealed record OpenWalletAccountCommand(
    Guid WalletId,
    Guid UserId,
    string Name,
    string Currency,
    decimal OpeningBalance) : ICommand;

public sealed record RenameWalletAccountCommand(Guid WalletId, string Name) : ICommand;

public sealed record ArchiveWalletAccountCommand(Guid WalletId) : ICommand;

public sealed record DeleteWalletAccountCommand(Guid WalletId) : ICommand;

internal sealed class SystemAccountHandlers(
    IAccountsDbContext dbContext,
    IAccountsEventBus eventBus)
    : ICommandHandler<ProvisionUserAccountsCommand>,
      ICommandHandler<OpenWalletAccountCommand>,
      ICommandHandler<RenameWalletAccountCommand>,
      ICommandHandler<ArchiveWalletAccountCommand>,
      ICommandHandler<DeleteWalletAccountCommand>
{
    public async Task Handle(ProvisionUserAccountsCommand command, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Accounts
            .Where(x => x.OwnerId == command.UserId)
            .Select(x => x.Purpose)
            .ToListAsync(cancellationToken);

        foreach (var purpose in Account.SystemAccounts.Keys.Except(existing))
        {
            var account = Account.CreateSystemAccount(command.UserId, purpose);
            dbContext.Accounts.Add(account);
            await eventBus.PublishAsync(AccountEvents.Created(account), cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(OpenWalletAccountCommand command, CancellationToken cancellationToken)
    {
        if (await FindWalletAccountAsync(command.WalletId, cancellationToken) is not null)
            return;

        var account = Account.CreateWalletAccount(
            command.UserId,
            command.WalletId,
            command.Name,
            Currencies.Parse(command.Currency));

        dbContext.Accounts.Add(account);
        await eventBus.PublishAsync(AccountEvents.Created(account, command.OpeningBalance), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(RenameWalletAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await GetWalletAccountAsync(command.WalletId, cancellationToken);

        account.Rename(command.Name);

        await eventBus.PublishAsync(new AccountRenamed(account.Id, account.OwnerId, account.Name), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ArchiveWalletAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await GetWalletAccountAsync(command.WalletId, cancellationToken);

        if (account.IsArchived)
            return;

        account.Archive();

        await eventBus.PublishAsync(new AccountArchived(account.Id, account.OwnerId), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(DeleteWalletAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await GetWalletAccountAsync(command.WalletId, cancellationToken);

        dbContext.Accounts.Remove(account);

        await eventBus.PublishAsync(new AccountDeleted(account.Id, account.OwnerId), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<Account?> FindWalletAccountAsync(Guid walletId, CancellationToken cancellationToken)
        => dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Purpose == AccountPurpose.Wallet && x.ReferenceId == walletId,
            cancellationToken);

    /// <remarks>
    /// Throws when the account does not exist yet, so the message is retried until the
    /// <c>WalletCreated</c> event that creates it has been processed.
    /// </remarks>
    private async Task<Account> GetWalletAccountAsync(Guid walletId, CancellationToken cancellationToken)
        => await FindWalletAccountAsync(walletId, cancellationToken)
           ?? throw new NotYetAvailableException($"The account of wallet '{walletId}' does not exist yet.");
}
