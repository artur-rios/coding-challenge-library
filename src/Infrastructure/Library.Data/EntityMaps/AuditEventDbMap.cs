using Library.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Library.Data.EntityMaps;

internal static class AuditEventDbMap
{
    public static void Configure(this EntityTypeBuilder<AuditEvent> auditEvent)
    {
        auditEvent.ToTable("audit_event");

        auditEvent.HasKey(entity => entity.Id);

        auditEvent.Property(entity => entity.PublicId).IsRequired();
        auditEvent.HasIndex(entity => entity.PublicId).IsUnique();

        auditEvent.Property(entity => entity.EntityType).IsRequired().HasMaxLength(50);
        auditEvent.Property(entity => entity.EntityId).IsRequired();
        auditEvent.Property(entity => entity.Action).IsRequired().HasMaxLength(100);
        auditEvent.Property(entity => entity.Actor).IsRequired().HasMaxLength(200);
        auditEvent.Property(entity => entity.OccurredAt).IsRequired();
        auditEvent.Property(entity => entity.CorrelationId).IsRequired().HasMaxLength(128);

        // jsonb rather than text: an auditor can then query inside the detail - "every loan of this
        // book that came back late" - instead of pulling every row out and parsing it client-side.
        auditEvent.Property(entity => entity.Data).IsRequired().HasColumnType("jsonb");

        // Deliberately no relationships. EntityId is a bare public identifier, so an entry survives
        // whatever it describes and no cascade can ever reach this table.

        // The two ways the trail is read: everything about one entity, and everything one request
        // caused.
        auditEvent.HasIndex(entity => new { entity.EntityType, entity.EntityId, entity.OccurredAt })
            .HasDatabaseName("ix_audit_event_entity");

        auditEvent.HasIndex(entity => entity.CorrelationId)
            .HasDatabaseName("ix_audit_event_correlation_id");

        auditEvent.HasIndex(entity => entity.OccurredAt)
            .HasDatabaseName("ix_audit_event_occurred_at");
    }
}
