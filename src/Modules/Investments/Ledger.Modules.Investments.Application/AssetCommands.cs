using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Investments.Contracts;
using Ledger.Modules.Investments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Investments.Application;

public sealed record CreateAssetCommand(Guid UserId, string Name, string? Symbol, AssetType Type, string Currency)
    : ICommand<AssetDto>;

public sealed record UpdateAssetCommand(Guid UserId, Guid AssetId, string Name, string? Symbol) : ICommand;

public sealed record ArchiveAssetCommand(Guid UserId, Guid AssetId) : ICommand;

/// <summary>Records the buy and/or sell price of an asset at a moment, without moving money.</summary>
public sealed record RecordPriceCommand(
    Guid UserId,
    Guid AssetId,
    decimal? BuyPrice,
    decimal? SellPrice,
    DateTimeOffset OccurredAt) : ICommand;

public sealed record GetAssetsQuery(Guid UserId, bool IncludeArchived) : IQuery<IReadOnlyList<AssetDto>>;

public sealed record GetAssetQuery(Guid UserId, Guid AssetId) : IQuery<AssetDto>;

public sealed record GetAssetPricesQuery(Guid UserId, Guid AssetId) : IQuery<IReadOnlyList<AssetPriceDto>>;

internal sealed class CreateAssetValidator : AbstractValidator<CreateAssetCommand>
{
    public CreateAssetValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Asset.NameMaxLength);
        RuleFor(x => x.Symbol).MaximumLength(Asset.SymbolMaxLength);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Currency)
            .Must(Currencies.IsSupported)
            .WithMessage($"Currency must be one of: {string.Join(", ", Enum.GetNames<Currency>())}.");
    }
}

internal sealed class UpdateAssetValidator : AbstractValidator<UpdateAssetCommand>
{
    public UpdateAssetValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Asset.NameMaxLength);
        RuleFor(x => x.Symbol).MaximumLength(Asset.SymbolMaxLength);
    }
}

internal sealed class RecordPriceValidator : AbstractValidator<RecordPriceCommand>
{
    public RecordPriceValidator()
    {
        RuleFor(x => x).Must(x => x.BuyPrice is not null || x.SellPrice is not null)
            .WithName("Price")
            .WithMessage("Enter the buy price, the sell price or both.");
        RuleFor(x => x.BuyPrice).GreaterThan(0).PrecisionScale(Prices.Precision, Prices.Decimals, true)
            .When(x => x.BuyPrice is not null);
        RuleFor(x => x.SellPrice).GreaterThan(0).PrecisionScale(Prices.Precision, Prices.Decimals, true)
            .When(x => x.SellPrice is not null);
        RuleFor(x => x.OccurredAt).NotInFuture();
    }
}

internal sealed class AssetHandlers(
    IInvestmentsDbContext dbContext,
    IInvestmentsEventBus eventBus)
    : ICommandHandler<CreateAssetCommand, AssetDto>,
      ICommandHandler<UpdateAssetCommand>,
      ICommandHandler<ArchiveAssetCommand>,
      ICommandHandler<RecordPriceCommand>,
      IQueryHandler<GetAssetsQuery, IReadOnlyList<AssetDto>>,
      IQueryHandler<GetAssetQuery, AssetDto>,
      IQueryHandler<GetAssetPricesQuery, IReadOnlyList<AssetPriceDto>>
{
    public async Task<AssetDto> Handle(CreateAssetCommand command, CancellationToken cancellationToken)
    {
        var asset = Asset.Create(
            command.UserId, command.Name, command.Symbol, command.Type, Currencies.Parse(command.Currency));

        dbContext.Assets.Add(asset);

        await eventBus.PublishAsync(
            new AssetCreated(asset.Id, asset.OwnerId, asset.Name, asset.Currency.ToString()), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return asset.ToDto([], DateTimeOffset.UtcNow);
    }

    public async Task Handle(UpdateAssetCommand command, CancellationToken cancellationToken)
    {
        var asset = await AssetLookup.GetOwnedAsync(dbContext, command.UserId, command.AssetId, cancellationToken);
        var renamed = asset.Name != command.Name.Trim();

        asset.Rename(command.Name, command.Symbol);

        if (renamed)
            await eventBus.PublishAsync(new AssetRenamed(asset.Id, asset.OwnerId, asset.Name), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ArchiveAssetCommand command, CancellationToken cancellationToken)
    {
        var asset = await AssetLookup.GetOwnedAsync(dbContext, command.UserId, command.AssetId, cancellationToken);

        asset.Archive();

        await eventBus.PublishAsync(new AssetArchived(asset.Id, asset.OwnerId), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(RecordPriceCommand command, CancellationToken cancellationToken)
    {
        var asset = await AssetLookup.GetOwnedAsync(dbContext, command.UserId, command.AssetId, cancellationToken);

        if (command.BuyPrice is { } buy)
            dbContext.Prices.Add(AssetPrice.Manual(asset, PriceSide.Buy, buy, command.OccurredAt));

        if (command.SellPrice is { } sell)
            dbContext.Prices.Add(AssetPrice.Manual(asset, PriceSide.Sell, sell, command.OccurredAt));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AssetDto>> Handle(GetAssetsQuery query, CancellationToken cancellationToken)
    {
        var assets = await dbContext.Assets
            .AsNoTracking()
            .Where(x => x.OwnerId == query.UserId && (query.IncludeArchived || x.Status == AssetStatus.Active))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var prices = await PricesByAssetAsync(query.UserId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        return assets
            .Select(a => a.ToDto(prices.TryGetValue(a.Id, out var list) ? list : [], now))
            .ToList();
    }

    public async Task<AssetDto> Handle(GetAssetQuery query, CancellationToken cancellationToken)
    {
        var asset = await AssetLookup.GetOwnedAsync(dbContext, query.UserId, query.AssetId, cancellationToken);
        var prices = await dbContext.Prices.AsNoTracking().Where(p => p.AssetId == asset.Id).ToListAsync(cancellationToken);

        return asset.ToDto(prices, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<AssetPriceDto>> Handle(GetAssetPricesQuery query, CancellationToken cancellationToken)
    {
        var asset = await AssetLookup.GetOwnedAsync(dbContext, query.UserId, query.AssetId, cancellationToken);

        var prices = await dbContext.Prices
            .AsNoTracking()
            .Where(p => p.AssetId == asset.Id)
            .OrderByDescending(p => p.OccurredAt).ThenByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return prices.Select(p => p.ToDto()).ToList();
    }

    private async Task<Dictionary<Guid, List<AssetPrice>>> PricesByAssetAsync(Guid userId, CancellationToken cancellationToken)
        => (await dbContext.Prices.AsNoTracking().Where(p => p.OwnerId == userId).ToListAsync(cancellationToken))
            .GroupBy(p => p.AssetId)
            .ToDictionary(g => g.Key, g => g.ToList());
}

internal static class AssetLookup
{
    public static async Task<Asset> GetOwnedAsync(
        IInvestmentsDbContext dbContext,
        Guid userId,
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets.SingleOrDefaultAsync(x => x.Id == assetId, cancellationToken);

        if (asset is null || asset.OwnerId != userId)
            throw new NotFoundException("Asset", assetId);

        return asset;
    }
}
