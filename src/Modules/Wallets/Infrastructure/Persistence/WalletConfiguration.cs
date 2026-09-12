using Ledger.Modules.Wallets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Modules.Wallets.Infrastructure.Persistence;

public sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("WALLETS");
        
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(w => w.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(w => w.UserId)
            .IsRequired();

        builder.Property(w => w.CreatedAt)
            .IsRequired();
    }
}