using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Accounts.Contracts;
using Ledger.Modules.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Accounts.Application.Investments;

/// <summary>Creates the asset account holding the cost of an investment asset. Idempotent.</summary>
public sealed record OpenInvestmentAccountCommand(Guid AssetId, Guid UserId, string Name, string Currency) : ICommand;

public sealed record RenameInvestmentAccountCommand(Guid AssetId, string Name) : ICommand;

public sealed record ArchiveInvestmentAccountCommand(Guid AssetId) : ICommand;

internal sealed class InvestmentAccountHandlers(
    IAccountsDbContext dbContext,
    IAccountsEventBus eventBus)
    : ICommandHandler<OpenInvestmentAccountCommand>,
      ICommandHandler<RenameInvestmentAccountCommand>,
      ICommandHandler<ArchiveInvestmentAccountCommand>
{
    public async Task Handle(OpenInvestmentAccountCommand command, CancellationToken cancellationToken)
    {
        if (await FindAsync(command.AssetId, cancellationToken) is not null)
            return;

        var account = Account.CreateInvestmentAccount(
            command.UserId, command.AssetId, command.Name, Currencies.Parse(command.Currency));

        dbContext.Accounts.Add(account);
        await eventBus.PublishAsync(AccountEvents.Created(account), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(RenameInvestmentAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await GetAsync(command.AssetId, cancellationToken);

        account.Rename(command.Name);

        await eventBus.PublishAsync(new AccountRenamed(account.Id, account.OwnerId, account.Name), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ArchiveInvestmentAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await GetAsync(command.AssetId, cancellationToken);

        if (account.IsArchived)
            return;

        account.Archive();

        await eventBus.PublishAsync(new AccountArchived(account.Id, account.OwnerId), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<Account?> FindAsync(Guid assetId, CancellationToken cancellationToken)
        => dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Purpose == AccountPurpose.Investment && x.ReferenceId == assetId,
            cancellationToken);

    /// <remarks>Retried until the <c>AssetCreated</c> event that creates the account has been processed.</remarks>
    private async Task<Account> GetAsync(Guid assetId, CancellationToken cancellationToken)
        => await FindAsync(assetId, cancellationToken)
           ?? throw new NotYetAvailableException($"The account of investment asset '{assetId}' does not exist yet.");
}
