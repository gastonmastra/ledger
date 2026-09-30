using Ledger.Modules.Wallets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Modules.Wallets.Infrastructure.Persistence;

internal sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("wallets");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.UserId)
            .IsRequired();

        builder.HasIndex(w => w.UserId);

        builder.Property(w => w.Name)
            .IsRequired()
            .HasMaxLength(Wallet.NameMaxLength);

        builder.Property(w => w.Currency)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(w => w.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(w => w.CreatedAt)
            .IsRequired();

        builder.Property(w => w.Balance)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(w => w.TransactionCount)
            .IsRequired();

        builder.Property(w => w.BalanceVersion)
            .IsRequired();

        builder.Ignore(w => w.IsReady);

        // Balance reports can be consumed concurrently: the xmin row version makes a stale
        // write fail (and be retried) instead of overwriting a newer balance.
        builder.Property<uint>("RowVersion")
            .IsRowVersion();
    }
}
