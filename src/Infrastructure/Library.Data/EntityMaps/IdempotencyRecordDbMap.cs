using Library.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Library.Data.EntityMaps;

internal static class IdempotencyRecordDbMap
{
    public static void Configure(this EntityTypeBuilder<IdempotencyRecord> record)
    {
        record.ToTable("idempotency_record");

        record.HasKey(entity => entity.Id);

        record.Property(entity => entity.Key).IsRequired().HasMaxLength(200);
        // This index is the idempotency guarantee. Not the lookup in the handler, which can be
        // overtaken between the read and the write - this, which the database enforces even when two
        // replicas insert at the same instant.
        record.HasIndex(entity => entity.Key).IsUnique().HasDatabaseName("ux_idempotency_record_key");

        record.Property(entity => entity.Endpoint).IsRequired().HasMaxLength(200);
        record.Property(entity => entity.RequestFingerprint).IsRequired().HasMaxLength(64);
        record.Property(entity => entity.StatusCode).IsRequired();
        // text, not jsonb - the one column in this schema where that is the right choice, and the
        // reason is the guarantee itself. PostgreSQL normalises jsonb on the way in: it reorders keys,
        // drops insignificant whitespace and rewrites numbers, so what comes back out is equivalent to
        // what went in but not identical to it. This column exists to replay a response verbatim, and
        // nothing ever queries inside it, so the storage format must preserve bytes rather than
        // structure.
        record.Property(entity => entity.ResponseBody).IsRequired().HasColumnType("text");
        record.Property(entity => entity.CreatedAt).IsRequired();
        record.Property(entity => entity.ExpiresAt).IsRequired();

        // The sweep's access path, so expiring old keys is a range scan rather than a table scan.
        record.HasIndex(entity => entity.ExpiresAt).HasDatabaseName("ix_idempotency_record_expires_at");
    }
}
