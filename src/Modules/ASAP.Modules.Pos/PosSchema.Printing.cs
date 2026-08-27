using ASAP.Modules.Pos.Printing;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Pos;

/// <summary>Registers the print template table.</summary>
public sealed partial class PosSchema
{
    private void ConfigurePrinting(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PrintTemplate>(builder =>
        {
            builder.ToTable("PrintTemplates", SchemaName);

            builder.Property(t => t.Code).HasMaxLength(32).IsRequired();
            builder.Property(t => t.Name).HasMaxLength(120).IsRequired();
            builder.Property(t => t.NameArabic).HasMaxLength(120);

            // No length limit. A receipt layout with a long footer about returns is an ordinary
            // thing, and a template truncated at some round number fails on the day somebody
            // adds a sentence rather than on the day somebody writes the limit.
            builder.Property(t => t.Content).IsRequired();

            builder.Property(t => t.RowVersion).IsRowVersion();

            builder.HasIndex(t => new { t.CompanyId, t.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // Choosing a template asks for the active ones of a kind, then picks the branch's
            // over the company's.
            builder.HasIndex(t => new { t.CompanyId, t.Kind, t.IsActive });
        });
    }
}
