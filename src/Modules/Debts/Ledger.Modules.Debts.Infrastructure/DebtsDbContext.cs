using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Debts.Application;
using Ledger.Modules.Debts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Modules.Debts.Infrastructure;

public sealed class DebtsDbContext(DbContextOptions<DebtsDbContext> options)
    : DbContext(options), IDebtsDbContext
{
    public const string Schema = "debts";

    public DbSet<DebtOperation> Operations => Set<DebtOperation>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<DebtPayment> Payments => Set<DebtPayment>();
    public DbSet<DebtContact> Contacts => Set<DebtContact>();
    public DbSet<DebtUser> Users => Set<DebtUser>();
    public DbSet<DebtGroupMember> GroupMembers => Set<DebtGroupMember>();
    public DbSet<DebtWallet> Wallets => Set<DebtWallet>();
    public DbSet<DebtBooksLock> BooksLocks => Set<DebtBooksLock>();
    public DbSet<OperationChange> Changes => Set<OperationChange>();
    public DbSet<PersonMerge> PersonMerges => Set<PersonMerge>();
    public DbSet<DebtLinkRequest> LinkRequests => Set<DebtLinkRequest>();

    public async Task ReplacePersonAsync(Guid previousPersonId, Guid userId, CancellationToken cancellationToken)
    {
        var from = previousPersonId;
        var to = userId;

        // Contacts: the user never keeps a contact for themselves, and an owner who already has a contact for the
        // user keeps that one (the other is merged). The rest become the user.
        await Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE debts.known_contacts SET "IsMerged" = TRUE, "LinkedUserId" = {to}
            WHERE "PersonId" = {from}
              AND ("OwnerId" = {to}
                   OR "OwnerId" IN (SELECT "OwnerId" FROM debts.known_contacts WHERE "PersonId" = {to} AND NOT "IsMerged"))
            """, cancellationToken);
        await Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE debts.known_contacts SET "PersonId" = {to}, "LinkedUserId" = {to}
            WHERE "PersonId" = {from} AND NOT "IsMerged"
            """, cancellationToken);

        await Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE debts.debts SET "CreditorPersonId" = {to} WHERE "CreditorPersonId" = {from}""", cancellationToken);
        await Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE debts.debts SET "DebtorPersonId" = {to} WHERE "DebtorPersonId" = {from}""", cancellationToken);
        await Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE debts.operations SET "FromPersonId" = {to} WHERE "FromPersonId" = {from}""", cancellationToken);
        await Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE debts.operations SET "ToPersonId" = {to} WHERE "ToPersonId" = {from}""", cancellationToken);
        await Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE debts.operation_payers SET "PersonId" = {to} WHERE "PersonId" = {from}""", cancellationToken);
        await Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE debts.operation_shares SET "PersonId" = {to} WHERE "PersonId" = {from}""", cancellationToken);
        await Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE debts.known_group_members
             SET "PersonId" = {to}, "UserId" = {to},
                 "DisplayName" = COALESCE((SELECT "DisplayName" FROM debts.known_users WHERE "UserId" = {to}), "DisplayName")
             WHERE "PersonId" = {from}
             """,
            cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.UseUtcDateTimeOffsets();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DebtsDbContext).Assembly);
        modelBuilder.AddMessagingTables();
    }
}

internal sealed class DebtOperationConfiguration : IEntityTypeConfiguration<DebtOperation>
{
    public void Configure(EntityTypeBuilder<DebtOperation> builder)
    {
        builder.ToTable("operations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Scope).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Total).HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(DebtOperation.DescriptionMaxLength);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Ignore(x => x.IsCurrent);
        builder.Ignore(x => x.InvolvedPersonIds);

        builder.OwnsMany(x => x.Payers, payer =>
        {
            payer.ToTable("operation_payers");
            payer.WithOwner().HasForeignKey("OperationId");
            payer.Property<int>("Id");
            payer.HasKey("Id");
            payer.Property(p => p.Amount).HasPrecision(19, 4);
        });

        builder.OwnsMany(x => x.Shares, share =>
        {
            share.ToTable("operation_shares");
            share.WithOwner().HasForeignKey("OperationId");
            share.Property<int>("Id");
            share.HasKey("Id");
            share.Property(s => s.Amount).HasPrecision(19, 4);
        });

        builder.Navigation(x => x.Payers).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Shares).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.GroupId, x.Status });
        builder.HasIndex(x => x.InitiatorUserId);
        builder.HasIndex(x => x.EntryId);
    }
}

internal sealed class PersonMergeConfiguration : IEntityTypeConfiguration<PersonMerge>, IEntityTypeConfiguration<DebtLinkRequest>
{
    public void Configure(EntityTypeBuilder<PersonMerge> builder)
    {
        builder.ToTable("person_merges");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Parties).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(PersonMerge.ReasonMaxLength);

        builder.HasIndex(x => x.RequestId).IsUnique();
        builder.HasIndex(x => new { x.PreviousPersonId, x.Status });
    }

    public void Configure(EntityTypeBuilder<DebtLinkRequest> builder)
    {
        builder.ToTable("known_link_requests");
        builder.HasKey(x => x.RequestId);
    }
}

internal sealed class OperationChangeConfiguration : IEntityTypeConfiguration<OperationChange>
{
    public void Configure(EntityTypeBuilder<OperationChange> builder)
    {
        builder.ToTable("operation_changes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(OperationChange.ReasonMaxLength).IsRequired();
        builder.Property(x => x.Draft).HasColumnType("jsonb");
        builder.Property(x => x.Preview).HasColumnType("jsonb");
        builder.Property(x => x.FailureReason).HasMaxLength(1000);
        builder.Ignore(x => x.AllAnswered);
        builder.Ignore(x => x.AcceptedUserIds);
        builder.Ignore(x => x.AnyRejected);

        builder.HasMany(x => x.Approvals)
            .WithOne()
            .HasForeignKey(x => x.ChangeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Approvals).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.OperationId);
        builder.HasIndex(x => x.RequestedByUserId);
    }
}

internal sealed class ChangeApprovalConfiguration : IEntityTypeConfiguration<ChangeApproval>
{
    public void Configure(EntityTypeBuilder<ChangeApproval> builder)
    {
        builder.ToTable("operation_change_approvals");

        builder.HasKey(x => new { x.ChangeId, x.UserId });

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(OperationChange.ReasonMaxLength);

        builder.HasIndex(x => x.UserId);
    }
}

internal sealed class DebtConfiguration : IEntityTypeConfiguration<Debt>
{
    public void Configure(EntityTypeBuilder<Debt> builder)
    {
        builder.ToTable("debts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount).HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.Paid).HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(x => x.IsSettling);
        builder.Ignore(x => x.IsPaid);
        builder.Ignore(x => x.Remaining);
        builder.Ignore(x => x.IsPartiallyPaid);

        builder.HasIndex(x => x.OperationId);
        builder.HasIndex(x => x.SettlementOperationId);
        builder.HasIndex(x => x.CreditorPersonId);
        builder.HasIndex(x => x.DebtorPersonId);
        builder.HasIndex(x => x.GroupId);
    }
}

internal sealed class DebtPaymentConfiguration : IEntityTypeConfiguration<DebtPayment>
{
    public void Configure(EntityTypeBuilder<DebtPayment> builder)
    {
        builder.ToTable("debt_payments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount).HasPrecision(19, 4).IsRequired();

        builder.HasIndex(x => x.DebtId);
        builder.HasIndex(x => x.SettlementOperationId);
    }
}

internal sealed class ProjectionConfigurations :
    IEntityTypeConfiguration<DebtContact>,
    IEntityTypeConfiguration<DebtUser>,
    IEntityTypeConfiguration<DebtGroupMember>,
    IEntityTypeConfiguration<DebtWallet>,
    IEntityTypeConfiguration<DebtBooksLock>
{
    public void Configure(EntityTypeBuilder<DebtBooksLock> builder)
    {
        builder.ToTable("known_books_locks");
        builder.HasKey(x => x.UserId);
    }

    public void Configure(EntityTypeBuilder<DebtContact> builder)
    {
        builder.ToTable("known_contacts");
        builder.HasKey(x => x.ContactId);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.OwnerId, x.PersonId });
    }

    public void Configure(EntityTypeBuilder<DebtUser> builder)
    {
        builder.ToTable("known_users");
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
    }

    public void Configure(EntityTypeBuilder<DebtGroupMember> builder)
    {
        builder.ToTable("known_group_members");
        builder.HasKey(x => new { x.GroupId, x.PersonId });
        builder.Property(x => x.GroupName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.UserId);
    }

    public void Configure(EntityTypeBuilder<DebtWallet> builder)
    {
        builder.ToTable("known_wallets");
        builder.HasKey(x => x.WalletId);
        builder.Property(x => x.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
    }
}
