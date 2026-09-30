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

        builder.HasIndex(x => new { x.OwnerId, x.PersonId })
            .IsUnique();
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
