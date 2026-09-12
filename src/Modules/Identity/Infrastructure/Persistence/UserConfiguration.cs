using Ledger.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Modules.Identity.Infrastructure.Persistence;

public sealed class UserConfiguration
    : IEntityTypeConfiguration<User>
{
    public void Configure(
        EntityTypeBuilder<User> builder)
    {
        builder.ToTable("USERS");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExternalId)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(x => x.ExternalId)
            .IsUnique();

        builder.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(x => x.CreatedAt)
            .IsRequired();
    }
}