using Microsoft.EntityFrameworkCore;

namespace Ledger.BuildingBlocks.Infrastructure.Messaging;

public static class MessagingModelBuilderExtensions
{
    /// <summary>Adds the integration outbox and the inbox tables to the module schema.</summary>
    public static ModelBuilder AddMessagingTables(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("integration_outbox");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Type).HasMaxLength(500).IsRequired();
            builder.Property(x => x.Content).HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.LastError).HasMaxLength(2000);
            builder.HasIndex(x => x.OccurredAt).HasFilter("\"ProcessedAt\" IS NULL");
        });

        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable("inbox_messages");
            builder.HasKey(x => new { x.MessageId, x.Consumer });
            builder.Property(x => x.Consumer).HasMaxLength(300);
        });

        return modelBuilder;
    }
}
