using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Groups.Application;
using Ledger.Modules.Groups.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Modules.Groups.Infrastructure;

public sealed class GroupsDbContext(DbContextOptions<GroupsDbContext> options)
    : DbContext(options), IGroupsDbContext
{
    public const string Schema = "groups";

    public DbSet<Group> Groups => Set<Group>();
    public DbSet<KnownContact> KnownContacts => Set<KnownContact>();
    public DbSet<KnownUser> KnownUsers => Set<KnownUser>();

    public Task ReplaceMemberAsync(Guid previousPersonId, Guid userId, string displayName, CancellationToken cancellationToken)
        => Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE groups.group_members
            SET "PersonId" = {userId}, "UserId" = {userId}, "DisplayName" = {displayName}
            WHERE "PersonId" = {previousPersonId}
            """, cancellationToken);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.UseUtcDateTimeOffsets();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GroupsDbContext).Assembly);
        modelBuilder.AddMessagingTables();
    }
}

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(Group.NameMaxLength)
            .IsRequired();

        builder.OwnsMany(x => x.Members, member =>
        {
            member.ToTable("group_members");
            member.WithOwner().HasForeignKey(m => m.GroupId);
            member.HasKey(m => new { m.GroupId, m.PersonId });
            member.Property(m => m.DisplayName).HasMaxLength(200).IsRequired();
            member.HasIndex(m => m.UserId);
        });

        builder.Navigation(x => x.Members)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class KnownContactConfiguration : IEntityTypeConfiguration<KnownContact>
{
    public void Configure(EntityTypeBuilder<KnownContact> builder)
    {
        builder.ToTable("known_contacts");
        builder.HasKey(x => x.ContactId);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.OwnerId);
    }
}

internal sealed class KnownUserConfiguration : IEntityTypeConfiguration<KnownUser>
{
    public void Configure(EntityTypeBuilder<KnownUser> builder)
    {
        builder.ToTable("known_users");
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
    }
}
