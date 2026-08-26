namespace ASAP.Platform.Kernel.Entities;

/// <summary>
/// Carries a row version so two users editing the same record cannot silently overwrite
/// each other. Mapped to a SQL Server <c>rowversion</c> column.
/// </summary>
public interface IConcurrencyAware
{
    /// <summary>Row version stamp, maintained by the database.</summary>
    byte[]? RowVersion { get; set; }
}
