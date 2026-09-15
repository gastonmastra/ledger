namespace Ledger.Modules.Transactions.Infrastructure.Configurations;

using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class TransactionConfiguration
    : IEntityTypeConfiguration<Transaction>
{
    public void Configure(
        EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                id => id.Value,
                value => new TransactionId(value));

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.Property(x => x.OccurredAt)
            .IsRequired();

        builder.HasMany(x => x.Postings)
            .WithOne()
            .HasForeignKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}