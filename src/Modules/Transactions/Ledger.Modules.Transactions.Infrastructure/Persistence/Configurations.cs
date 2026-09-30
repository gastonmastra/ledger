using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Modules.Transactions.Infrastructure.Persistence;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new TransactionId(value));

        builder.Property(x => x.OwnerId)
            .IsRequired();

        builder.Property(x => x.OccurredAt)
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(Transaction.DescriptionMaxLength);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasMany(x => x.Postings)
            .WithOne()
            .HasForeignKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Postings)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.OwnerId, x.OccurredAt });
        builder.HasIndex(x => x.CorrelationId);
    }
}

internal sealed class PostingConfiguration : IEntityTypeConfiguration<Posting>
{
    public void Configure(EntityTypeBuilder<Posting> builder)
    {
        builder.ToTable("postings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new PostingId(value));

        builder.Property(x => x.TransactionId)
            .HasConversion(id => id.Value, value => new TransactionId(value));

        builder.Property(x => x.AccountId)
            .HasConversion(id => id.Value, value => new AccountId(value));

        builder.Property(x => x.Amount)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(x => x.Currency)
            .HasConversion<string>()
            .HasMaxLength(3)
            .IsRequired();

        builder.HasIndex(x => x.AccountId);
        builder.HasIndex(x => x.CounterpartyId);
    }
}

internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> builder)
    {
        builder.ToTable("ledger_accounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new AccountId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
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

        builder.Property(x => x.Balance)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Ignore(x => x.IsWallet);

        builder.HasIndex(x => new { x.OwnerId, x.Purpose });
        builder.HasIndex(x => x.ReferenceId);
    }
}

internal sealed class PendingWalletAssignmentConfiguration : IEntityTypeConfiguration<PendingWalletAssignment>
{
    public void Configure(EntityTypeBuilder<PendingWalletAssignment> builder)
    {
        builder.ToTable("pending_wallet_assignments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(x => x.Currency)
            .HasConversion<string>()
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(Transaction.DescriptionMaxLength);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(x => new { x.OwnerId, x.Status });
    }
}

internal sealed class CounterpartyConfiguration : IEntityTypeConfiguration<Counterparty>
{
    public void Configure(EntityTypeBuilder<Counterparty> builder)
    {
        builder.ToTable("counterparties");

        builder.HasKey(x => new { x.OwnerId, x.PersonId });

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(x => new { x.OwnerId, x.SourceId });
    }
}
