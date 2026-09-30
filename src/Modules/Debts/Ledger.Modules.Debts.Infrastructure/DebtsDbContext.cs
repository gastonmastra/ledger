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
    public DbSet<DebtContact> Contacts => Set<DebtContact>();
    public DbSet<DebtUser> Users => Set<DebtUser>();
    public DbSet<DebtGroupMember> GroupMembers => Set<DebtGroupMember>();
    public DbSet<DebtWallet> Wallets => Set<DebtWallet>();

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
    }
}

internal sealed class DebtConfiguration : IEntityTypeConfiguration<Debt>
{
    public void Configure(EntityTypeBuilder<Debt> builder)
    {
        builder.ToTable("debts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount).HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.Currency).HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(x => x.IsSettling);

        builder.HasIndex(x => x.OperationId);
        builder.HasIndex(x => x.SettlementOperationId);
        builder.HasIndex(x => x.CreditorPersonId);
        builder.HasIndex(x => x.DebtorPersonId);
        builder.HasIndex(x => x.GroupId);
    }
}

internal sealed class ProjectionConfigurations :
    IEntityTypeConfiguration<DebtContact>,
    IEntityTypeConfiguration<DebtUser>,
    IEntityTypeConfiguration<DebtGroupMember>,
    IEntityTypeConfiguration<DebtWallet>
{
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
