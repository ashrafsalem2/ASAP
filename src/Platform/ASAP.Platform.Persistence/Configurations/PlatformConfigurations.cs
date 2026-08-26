using ASAP.Platform.Core.Auditing;
using ASAP.Platform.Core.Dimensions;
using ASAP.Platform.Core.Events;
using ASAP.Platform.Core.Numbering;
using ASAP.Platform.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ASAP.Platform.Persistence.Configurations;

/// <summary>Maps <see cref="SetupValue"/>.</summary>
public sealed class SetupValueConfiguration : IEntityTypeConfiguration<SetupValue>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SetupValue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SetupValues");

        builder.Property(s => s.Key).HasMaxLength(128).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(4000);
        builder.Property(s => s.RowVersion).IsRowVersion();

        // One value per setting per scope. ScopeId is null for a tenant-wide value, and SQL
        // Server treats nulls as equal in a unique index, which is exactly what is wanted here:
        // a tenant may hold only one value for a given key.
        builder.HasIndex(s => new { s.TenantId, s.Key, s.Scope, s.ScopeId }).IsUnique();
    }
}

/// <summary>Maps <see cref="NumberSeries"/>.</summary>
public sealed class NumberSeriesConfiguration : IEntityTypeConfiguration<NumberSeries>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NumberSeries> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("NumberSeries");

        builder.Property(n => n.Code).HasMaxLength(64).IsRequired();
        builder.Property(n => n.Description).HasMaxLength(200).IsRequired();
        builder.Property(n => n.DescriptionArabic).HasMaxLength(200);
        builder.Property(n => n.RowVersion).IsRowVersion();

        builder.HasIndex(n => new { n.CompanyId, n.Code })
               .IsUnique()
               .HasFilter("[IsDeleted] = 0");

        builder.HasMany(n => n.Lines)
               .WithOne(l => l.NumberSeries!)
               .HasForeignKey(l => l.NumberSeriesId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="NumberSeriesLine"/>.</summary>
public sealed class NumberSeriesLineConfiguration : IEntityTypeConfiguration<NumberSeriesLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NumberSeriesLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("NumberSeriesLines");

        builder.Property(l => l.StartingNumber).HasMaxLength(64).IsRequired();
        builder.Property(l => l.EndingNumber).HasMaxLength(64);
        builder.Property(l => l.LastNumberUsed).HasMaxLength(64);
        builder.Property(l => l.RowVersion).IsRowVersion();

        // The allocator finds the line in force for a document date by taking the latest
        // StartingDate at or before it, so this index carries every allocation.
        builder.HasIndex(l => new { l.NumberSeriesId, l.StartingDate });
    }
}

/// <summary>Maps <see cref="Dimension"/>.</summary>
public sealed class DimensionConfiguration : IEntityTypeConfiguration<Dimension>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Dimension> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Dimensions");

        builder.Property(d => d.Code).HasMaxLength(32).IsRequired();
        builder.Property(d => d.Name).HasMaxLength(100).IsRequired();
        builder.Property(d => d.NameArabic).HasMaxLength(100);
        builder.Property(d => d.Description).HasMaxLength(500);
        builder.Property(d => d.RowVersion).IsRowVersion();

        builder.HasIndex(d => new { d.CompanyId, d.Code })
               .IsUnique()
               .HasFilter("[IsDeleted] = 0");

        // A shortcut position is copied onto every ledger entry, so two dimensions cannot share
        // one. Filtered to the non-null positions, since ordinary dimensions all have none.
        builder.HasIndex(d => new { d.CompanyId, d.ShortcutIndex })
               .IsUnique()
               .HasFilter("[ShortcutIndex] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasMany(d => d.Values)
               .WithOne(v => v.Dimension!)
               .HasForeignKey(v => v.DimensionId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="DimensionValue"/>.</summary>
public sealed class DimensionValueConfiguration : IEntityTypeConfiguration<DimensionValue>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DimensionValue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DimensionValues");

        builder.Property(v => v.Code).HasMaxLength(32).IsRequired();
        builder.Property(v => v.Name).HasMaxLength(100).IsRequired();
        builder.Property(v => v.NameArabic).HasMaxLength(100);
        builder.Property(v => v.TotalRange).HasMaxLength(200);
        builder.Property(v => v.RowVersion).IsRowVersion();

        builder.HasIndex(v => new { v.DimensionId, v.Code })
               .IsUnique()
               .HasFilter("[IsDeleted] = 0");

        builder.Ignore(v => v.IsPostable);
    }
}

/// <summary>Maps <see cref="DimensionSet"/>.</summary>
public sealed class DimensionSetConfiguration : IEntityTypeConfiguration<DimensionSet>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DimensionSet> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DimensionSets");

        builder.Property(s => s.Fingerprint).HasColumnType("binary(32)").IsRequired();
        builder.Property(s => s.Signature).HasMaxLength(2000).IsRequired();

        // Every posting resolves its combination through this index before writing an entry, so
        // it is one of the hottest in the database. A 32-byte key keeps it compact; the readable
        // signature is deliberately left out of it.
        builder.HasIndex(s => new { s.CompanyId, s.Fingerprint }).IsUnique();

        builder.HasMany(s => s.Entries)
               .WithOne(e => e.DimensionSet!)
               .HasForeignKey(e => e.DimensionSetId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="DimensionSetEntry"/>.</summary>
public sealed class DimensionSetEntryConfiguration : IEntityTypeConfiguration<DimensionSetEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DimensionSetEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DimensionSetEntries");

        builder.HasIndex(e => new { e.DimensionSetId, e.DimensionId }).IsUnique();

        builder.HasOne(e => e.Dimension!)
               .WithMany()
               .HasForeignKey(e => e.DimensionId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.DimensionValue!)
               .WithMany()
               .HasForeignKey(e => e.DimensionValueId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <see cref="AuditLogEntry"/>.</summary>
public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AuditLog");

        builder.Property(a => a.UserName).HasMaxLength(128);
        builder.Property(a => a.EntityType).HasMaxLength(128);
        builder.Property(a => a.DisplayNo).HasMaxLength(64);
        builder.Property(a => a.Changes).HasColumnType("nvarchar(max)");
        builder.Property(a => a.OverriddenMessageCode).HasMaxLength(96);
        builder.Property(a => a.OverrideReason).HasMaxLength(1000);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.ClientKind).HasMaxLength(32);

        // The three questions actually asked of an audit log: what happened to this record, what
        // did this person do, and who overrode a protection.
        builder.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        builder.HasIndex(a => new { a.TenantId, a.UserId, a.OccurredAtUtc });
        builder.HasIndex(a => new { a.TenantId, a.OverriddenMessageCode, a.OccurredAtUtc })
               .HasFilter("[OverriddenMessageCode] IS NOT NULL");
    }
}

/// <summary>Maps <see cref="OutboxMessage"/>.</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Outbox");

        builder.Property(o => o.EventName).HasMaxLength(128).IsRequired();
        builder.Property(o => o.EventType).HasMaxLength(512).IsRequired();
        builder.Property(o => o.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(o => o.LastError).HasMaxLength(4000);

        // The worker claims work with this index alone: unprocessed, not given up on, and due.
        // Filtering out processed rows keeps it small however large the table grows.
        builder.HasIndex(o => new { o.ProcessedAtUtc, o.NextAttemptAtUtc })
               .HasFilter("[ProcessedAtUtc] IS NULL AND [IsDeadLettered] = 0");
    }
}

/// <summary>Maps <see cref="Core.Numbering.TransactionCounter"/>.</summary>
public sealed class TransactionCounterConfiguration
    : IEntityTypeConfiguration<Core.Numbering.TransactionCounter>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Core.Numbering.TransactionCounter> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TransactionCounters");

        // One row per company, found by company rather than by key on every posting.
        builder.HasIndex(c => c.CompanyId).IsUnique();
    }
}
