using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Wallets.Contracts;
using Ledger.Modules.Wallets.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Wallets.Application;

public sealed record WalletDto(
    Guid Id,
    string Name,
    string Currency,
    decimal Balance,
    WalletStatus Status,
    DateTimeOffset CreatedAt,
    bool IsReady);

public sealed record CreateWalletCommand(Guid UserId, string Name, string Currency, decimal OpeningBalance)
    : ICommand<WalletDto>;

public sealed record RenameWalletCommand(Guid UserId, Guid WalletId, string Name) : ICommand;

public sealed record ArchiveWalletCommand(Guid UserId, Guid WalletId) : ICommand;

public sealed record DeleteWalletCommand(Guid UserId, Guid WalletId) : ICommand;

public sealed record GetWalletsQuery(Guid UserId, bool IncludeArchived) : IQuery<IReadOnlyList<WalletDto>>;

public sealed record GetWalletQuery(Guid UserId, Guid WalletId) : IQuery<WalletDto>;

/// <summary>Internal: keeps the balance projection in sync with the ledger.</summary>
public sealed record ApplyWalletBalanceCommand(Guid WalletId, decimal Balance, int TransactionCount, long Version)
    : ICommand;

internal sealed class CreateWalletValidator : AbstractValidator<CreateWalletCommand>
{
    public CreateWalletValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Wallet.NameMaxLength);
        RuleFor(x => x.Currency)
            .Must(Currencies.IsSupported)
            .WithMessage($"Currency must be one of: {string.Join(", ", Enum.GetNames<Currency>())}.");
        RuleFor(x => x.OpeningBalance).GreaterThanOrEqualTo(0);
    }
}

internal sealed class RenameWalletValidator : AbstractValidator<RenameWalletCommand>
{
    public RenameWalletValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Wallet.NameMaxLength);
    }
}

internal sealed class WalletHandlers(
    IWalletsDbContext dbContext,
    IWalletsEventBus eventBus)
    : ICommandHandler<CreateWalletCommand, WalletDto>,
      ICommandHandler<RenameWalletCommand>,
      ICommandHandler<ArchiveWalletCommand>,
      ICommandHandler<DeleteWalletCommand>,
      ICommandHandler<ApplyWalletBalanceCommand>,
      IQueryHandler<GetWalletsQuery, IReadOnlyList<WalletDto>>,
      IQueryHandler<GetWalletQuery, WalletDto>
{
    public async Task<WalletDto> Handle(CreateWalletCommand command, CancellationToken cancellationToken)
    {
        var wallet = Wallet.Create(
            command.UserId,
            command.Name,
            Currencies.Parse(command.Currency),
            command.OpeningBalance);

        dbContext.Wallets.Add(wallet);

        await eventBus.PublishAsync(
            new WalletCreated(
                wallet.Id,
                wallet.UserId,
                wallet.Name,
                wallet.Currency.ToString(),
                command.OpeningBalance,
                wallet.CreatedAt),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(wallet);
    }

    public async Task Handle(RenameWalletCommand command, CancellationToken cancellationToken)
    {
        var wallet = await GetOwnedAsync(command.UserId, command.WalletId, cancellationToken);

        wallet.Rename(command.Name);

        await eventBus.PublishAsync(new WalletRenamed(wallet.Id, wallet.UserId, wallet.Name), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ArchiveWalletCommand command, CancellationToken cancellationToken)
    {
        var wallet = await GetOwnedAsync(command.UserId, command.WalletId, cancellationToken);

        wallet.Archive();

        await eventBus.PublishAsync(new WalletArchived(wallet.Id, wallet.UserId), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(DeleteWalletCommand command, CancellationToken cancellationToken)
    {
        var wallet = await GetOwnedAsync(command.UserId, command.WalletId, cancellationToken);

        wallet.EnsureCanBeDeleted();

        dbContext.Wallets.Remove(wallet);

        await eventBus.PublishAsync(new WalletDeleted(wallet.Id, wallet.UserId), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ApplyWalletBalanceCommand command, CancellationToken cancellationToken)
    {
        var wallet = await dbContext.Wallets.SingleOrDefaultAsync(x => x.Id == command.WalletId, cancellationToken);

        // The wallet may have been deleted meanwhile; nothing to project then.
        if (wallet is null)
            return;

        wallet.ApplyLedgerBalance(command.Balance, command.TransactionCount, command.Version);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WalletDto>> Handle(GetWalletsQuery query, CancellationToken cancellationToken)
    {
        var wallets = await dbContext.Wallets
            .AsNoTracking()
            .Where(x => x.UserId == query.UserId && (query.IncludeArchived || x.Status == WalletStatus.Active))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return wallets.Select(ToDto).ToList();
    }

    public async Task<WalletDto> Handle(GetWalletQuery query, CancellationToken cancellationToken)
        => ToDto(await GetOwnedAsync(query.UserId, query.WalletId, cancellationToken));

    private async Task<Wallet> GetOwnedAsync(Guid userId, Guid walletId, CancellationToken cancellationToken)
    {
        var wallet = await dbContext.Wallets.SingleOrDefaultAsync(x => x.Id == walletId, cancellationToken);

        if (wallet is null || wallet.UserId != userId)
            throw new NotFoundException("Wallet", walletId);

        return wallet;
    }

    private static WalletDto ToDto(Wallet wallet)
        => new(
            wallet.Id,
            wallet.Name,
            wallet.Currency.ToString(),
            wallet.Balance,
            wallet.Status,
            wallet.CreatedAt,
            wallet.IsReady);
}
