using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Contacts.Application;
using Ledger.Modules.Contacts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Modules.Contacts.Infrastructure.Persistence;

public sealed class ContactsDbContext(DbContextOptions<ContactsDbContext> options)
    : DbContext(options), IContactsDbContext
{
    public const string Schema = "contacts";

    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<RegisteredUser> RegisteredUsers => Set<RegisteredUser>();
    public DbSet<ContactLinkRequest> LinkRequests => Set<ContactLinkRequest>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.UseUtcDateTimeOffsets();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContactsDbContext).Assembly);
        modelBuilder.AddMessagingTables();
    }
}

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(Contact.NameMaxLength)
            .IsRequired();

        builder.Property(x => x.Email)
            .HasMaxLength(320);

        builder.Property(x => x.Origin)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Ignore(x => x.IsMerged);

        builder.HasIndex(x => new { x.OwnerId, x.PersonId })
            .IsUnique();

        builder.HasIndex(x => x.PersonId);
    }
}

internal sealed class ContactLinkRequestConfiguration : IEntityTypeConfiguration<ContactLinkRequest>
{
    public void Configure(EntityTypeBuilder<ContactLinkRequest> builder)
    {
        builder.ToTable("link_requests");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ContactName)
            .HasMaxLength(Contact.NameMaxLength)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.FailureReason)
            .HasMaxLength(ContactLinkRequest.ReasonMaxLength);

        builder.Ignore(x => x.IsOpen);

        builder.HasIndex(x => x.OwnerId);
        builder.HasIndex(x => x.TargetUserId);
        builder.HasIndex(x => x.ContactId);
    }
}

internal sealed class RegisteredUserConfiguration : IEntityTypeConfiguration<RegisteredUser>
{
    public void Configure(EntityTypeBuilder<RegisteredUser> builder)
    {
        builder.ToTable("registered_users");

        builder.HasKey(x => x.UserId);

        builder.Property(x => x.Email)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(x => x.Email);
    }
}
