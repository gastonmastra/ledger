using Ledger.Modules.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Modules.Accounts.Infrastructure.Persistence;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OwnerId)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(Account.NameMaxLength)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Purpose)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Currency)
            .HasConversion<string>()
            .HasMaxLength(3);

        builder.Property(x => x.IsArchived)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Ignore(x => x.IsSystem);

        builder.HasIndex(x => new { x.OwnerId, x.Purpose });

        builder.HasIndex(x => x.ReferenceId)
            .IsUnique()
            .HasFilter("\"ReferenceId\" IS NOT NULL");
    }
}
