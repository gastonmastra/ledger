using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Transactions.Contracts;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Projections;

public sealed record ProjectAccountCommand(
    Guid AccountId,
    Guid OwnerId,
    string Name,
    string Type,
    string Purpose,
    string? Currency,
    Guid? ReferenceId) : ICommand;

public sealed record RenameAccountProjectionCommand(Guid AccountId, string Name) : ICommand;

public sealed record ArchiveAccountProjectionCommand(Guid AccountId) : ICommand;

public sealed record DeleteAccountProjectionCommand(Guid AccountId) : ICommand;

public sealed record ProjectCounterpartyCommand(
    Guid OwnerId,
    Guid PersonId,
    CounterpartyKind Kind,
    Guid SourceId,
    string Name,
    Guid? PreviousPersonId = null) : ICommand;

internal sealed class ProjectionHandlers(
    ITransactionsDbContext dbContext,
    ITransactionsEventBus eventBus)
    : ICommandHandler<ProjectAccountCommand>,
      ICommandHandler<RenameAccountProjectionCommand>,
      ICommandHandler<ArchiveAccountProjectionCommand>,
      ICommandHandler<DeleteAccountProjectionCommand>,
      ICommandHandler<ProjectCounterpartyCommand>
{
    public async Task Handle(ProjectAccountCommand command, CancellationToken cancellationToken)
    {
        var id = new AccountId(command.AccountId);

        if (await dbContext.LedgerAccounts.AnyAsync(x => x.Id == id, cancellationToken))
            return;

        var account = LedgerAccount.Create(
            id,
            command.OwnerId,
            command.Name,
            Enum.Parse<LedgerAccountType>(command.Type),
            Enum.Parse<LedgerAccountPurpose>(command.Purpose),
            command.Currency is null ? null : Currencies.Parse(command.Currency),
            command.ReferenceId);

        dbContext.LedgerAccounts.Add(account);

        if (account.IsWallet)
        {
            // Tells the Wallets module that the wallet can be used for transactions.
            await eventBus.PublishAsync(
                new WalletBalanceChanged(account.ReferenceId!.Value, account.Balance, account.TransactionCount, account.Version),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(RenameAccountProjectionCommand command, CancellationToken cancellationToken)
    {
        var account = await GetAsync(command.AccountId, cancellationToken);
        account.Rename(command.Name);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ArchiveAccountProjectionCommand command, CancellationToken cancellationToken)
    {
        var account = await GetAsync(command.AccountId, cancellationToken);
        account.Archive();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(DeleteAccountProjectionCommand command, CancellationToken cancellationToken)
    {
        var id = new AccountId(command.AccountId);
        var account = await dbContext.LedgerAccounts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (account is null)
            return;

        if (await dbContext.Postings.AnyAsync(x => x.AccountId == id, cancellationToken))
        {
            // Should not happen (wallets with transactions cannot be deleted); keep history intact.
            account.Archive();
        }
        else
        {
            dbContext.LedgerAccounts.Remove(account);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ProjectCounterpartyCommand command, CancellationToken cancellationToken)
    {
        if (command.PreviousPersonId is { } previous)
        {
            var old = await dbContext.Counterparties.SingleOrDefaultAsync(
                x => x.OwnerId == command.OwnerId && x.PersonId == previous, cancellationToken);

            if (old is not null)
                dbContext.Counterparties.Remove(old);
        }

        var existing = await dbContext.Counterparties.SingleOrDefaultAsync(
            x => x.OwnerId == command.OwnerId && x.PersonId == command.PersonId, cancellationToken);

        if (existing is null)
            dbContext.Counterparties.Add(Counterparty.Create(
                command.OwnerId, command.PersonId, command.Kind, command.SourceId, command.Name));
        else
            existing.Rename(command.Name);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<LedgerAccount> GetAsync(Guid accountId, CancellationToken cancellationToken)
        => await dbContext.LedgerAccounts.SingleOrDefaultAsync(x => x.Id == new AccountId(accountId), cancellationToken)
           ?? throw new BuildingBlocks.Application.Exceptions.NotYetAvailableException(
               $"Ledger account '{accountId}' is not projected yet.");
}
