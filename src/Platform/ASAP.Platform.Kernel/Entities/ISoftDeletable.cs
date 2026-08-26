namespace ASAP.Platform.Kernel.Entities;

/// <summary>
/// Marks master data that is hidden rather than physically removed. Ledger entries are never
/// soft-deletable: once posted, an entry is corrected by reversal, never by deletion.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>True once the row has been deleted. Filtered out of every query by default.</summary>
    bool IsDeleted { get; set; }

    /// <summary>When the row was deleted, in UTC.</summary>
    DateTime? DeletedAtUtc { get; set; }

    /// <summary>User who deleted the row.</summary>
    Guid? DeletedBy { get; set; }
}
