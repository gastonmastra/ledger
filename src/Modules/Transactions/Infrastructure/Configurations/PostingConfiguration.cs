namespace Ledger.Modules.Transactions.Infrastructure.Configurations;

using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class PostingConfiguration
    : IEntityTypeConfiguration<Posting>
{
    public void Configure(
        EntityTypeBuilder<Posting> builder)
    {
        builder.ToTable("postings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                id => id.Value,
                value => new PostingId(value));

        builder.Property(x => x.TransactionId)
            .HasConversion(
                id => id.Value,
                value => new TransactionId(value));

        builder.Property(x => x.AccountId)
            .HasConversion(
                id => id.Value,
                value => new AccountId(value));

        builder.Property(x => x.Amount)
            .HasPrecision(19, 4)
            .IsRequired();
    }
}