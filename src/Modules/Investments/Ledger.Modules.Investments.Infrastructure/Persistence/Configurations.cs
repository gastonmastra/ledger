using Ledger.Modules.Investments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Modules.Investments.Infrastructure.Persistence;

internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("assets");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OwnerId).IsRequired();
        builder.HasIndex(x => x.OwnerId);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(Asset.NameMaxLength);
        builder.Property(x => x.Symbol).HasMaxLength(Asset.SymbolMaxLength);
        builder.Property(x => x.Type).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.Currency).HasConversion<string>().IsRequired().HasMaxLength(3);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.Property(x => x.Quantity).HasPrecision(Quantities.Precision, Quantities.Decimals).IsRequired();
        builder.Property(x => x.CostBasis).HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.RealizedGain).HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.LastTradeAt);
        builder.Property(x => x.PendingTradeId);

        builder.Ignore(x => x.AverageCost);

        // Two trades of the same asset started at the same time: the second save fails instead of
        // both reserving the asset.
        builder.Property<uint>("RowVersion").IsRowVersion();
    }
}

internal sealed class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.ToTable("trades");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OwnerId).IsRequired();
        builder.Property(x => x.AssetId).IsRequired();
        builder.HasIndex(x => new { x.OwnerId, x.OccurredAt });
        builder.HasIndex(x => new { x.AssetId, x.OccurredAt });

        builder.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Kind).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.Quantity).HasPrecision(Quantities.Precision, Quantities.Decimals).IsRequired();
        builder.Property(x => x.UnitPrice).HasPrecision(Prices.Precision, Prices.Decimals).IsRequired();
        builder.Property(x => x.Total).HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.Currency).HasConversion<string>().IsRequired().HasMaxLength(3);
        builder.Property(x => x.WalletId);
        builder.Property(x => x.WalletCurrency).HasConversion<string>().HasMaxLength(3);
        builder.Property(x => x.WalletAmount).HasPrecision(19, 4);
        builder.Property(x => x.CostRemoved).HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.CounterPrice).HasPrecision(Prices.Precision, Prices.Decimals);
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(Trade.DescriptionMaxLength);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.RejectionReason).HasMaxLength(Trade.ReasonMaxLength);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.Ignore(x => x.RealizedGain);
        builder.Ignore(x => x.Side);
    }
}

internal sealed class AssetPriceConfiguration : IEntityTypeConfiguration<AssetPrice>
{
    public void Configure(EntityTypeBuilder<AssetPrice> builder)
    {
        builder.ToTable("asset_prices");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.AssetId).IsRequired();
        builder.Property(x => x.OwnerId).IsRequired();
        builder.HasIndex(x => new { x.AssetId, x.OccurredAt });
        builder.HasIndex(x => x.OwnerId);

        builder.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.Side).HasConversion<string>().IsRequired().HasMaxLength(10);
        builder.Property(x => x.Price).HasPrecision(Prices.Precision, Prices.Decimals).IsRequired();
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Source).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.TradeId);
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
