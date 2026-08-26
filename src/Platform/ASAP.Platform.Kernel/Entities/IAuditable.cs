namespace ASAP.Platform.Kernel.Entities;

/// <summary>
/// Records who created and last changed a row. The persistence layer fills these in
/// automatically on save, so no handler or module ever has to remember to set them.
/// </summary>
public interface IAuditable
{
    /// <summary>When the row was first written, in UTC.</summary>
    DateTime CreatedAtUtc { get; set; }

    /// <summary>User who created the row. Null only for rows written by the seeder or a system job.</summary>
    Guid? CreatedBy { get; set; }

    /// <summary>When the row was last changed, in UTC. Null while the row is still as created.</summary>
    DateTime? ModifiedAtUtc { get; set; }

    /// <summary>User who last changed the row.</summary>
    Guid? ModifiedBy { get; set; }
}
