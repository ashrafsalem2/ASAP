using ASAP.Modules.Hr.Payroll;
using ASAP.Modules.Hr.People;
using ASAP.Platform.Persistence;
using ASAP.Platform.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Hr;

/// <summary>
/// Registers the human resources tables.
/// </summary>
/// <remarks>
/// Everything lands in the <c>hr</c> schema, so it is obvious in the database which module owns
/// what once a dozen are installed — and so the tables holding people's identity numbers and pay
/// are somewhere a database administrator can grant separately from the rest.
/// </remarks>
public sealed class HrSchema : IModuleSchema
{
    /// <inheritdoc />
    public string SchemaName => "hr";

    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Position>(builder =>
        {
            builder.ToTable("Positions", SchemaName);

            builder.Property(p => p.Code).HasMaxLength(20).IsRequired();
            builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
            builder.Property(p => p.TitleArabic).HasMaxLength(200);
            builder.Property(p => p.Department).HasMaxLength(100);

            builder.HasIndex(p => new { p.CompanyId, p.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<Employee>(builder =>
        {
            builder.ToTable("Employees", SchemaName);

            builder.Property(e => e.No).HasMaxLength(20).IsRequired();
            builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
            builder.Property(e => e.NameArabic).HasMaxLength(200);
            builder.Property(e => e.NationalId).HasMaxLength(30);
            builder.Property(e => e.Nationality).HasMaxLength(60);
            builder.Property(e => e.Email).HasMaxLength(256);
            builder.Property(e => e.Phone).HasMaxLength(40);

            builder.Property(e => e.BasicWage).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(e => e.Allowances).HasColumnType(DecimalPrecisionConventions.Money);

            builder.HasIndex(e => new { e.CompanyId, e.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // "Who works here now" is the question every screen asks first.
            builder.HasIndex(e => new { e.CompanyId, e.Status });

            builder.HasOne(e => e.Position)
                   .WithMany()
                   .HasForeignKey(e => e.PositionId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(e => e.BranchAssignments)
                   .WithOne(a => a.Employee!)
                   .HasForeignKey(a => a.EmployeeId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Ignore(e => e.TotalWage);
            builder.Ignore(e => e.IsEmployed);
        });

        modelBuilder.Entity<PayrollRun>(builder =>
        {
            builder.ToTable("PayrollRuns", SchemaName);

            builder.Property(r => r.No).HasMaxLength(20).IsRequired();
            builder.Property(r => r.Description).HasMaxLength(250);

            builder.HasIndex(r => new { r.CompanyId, r.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            builder.HasIndex(r => new { r.CompanyId, r.FromDate });

            builder.HasMany(r => r.Lines)
                   .WithOne(l => l.PayrollRun!)
                   .HasForeignKey(l => l.PayrollRunId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Ignore(r => r.DaysInPeriod);
            builder.Ignore(r => r.GrossPay);
            builder.Ignore(r => r.Deductions);
            builder.Ignore(r => r.NetPay);
            builder.Ignore(r => r.EndOfServiceCharge);
            builder.Ignore(r => r.IsEditable);
        });

        modelBuilder.Entity<PayrollLine>(builder =>
        {
            builder.ToTable("PayrollLines", SchemaName);

            builder.Property(l => l.EmployeeNo).HasMaxLength(20).IsRequired();
            builder.Property(l => l.EmployeeName).HasMaxLength(200).IsRequired();
            builder.Property(l => l.Note).HasMaxLength(500);

            foreach (var money in new[]
                     {
                         nameof(PayrollLine.BasicPay),
                         nameof(PayrollLine.Allowances),
                         nameof(PayrollLine.OtherEarnings),
                         nameof(PayrollLine.Deductions),
                         nameof(PayrollLine.EndOfServiceCharge),
                     })
            {
                builder.Property(money).HasColumnType(DecimalPrecisionConventions.Money);
            }

            // "What has this person been paid" reads this one, which is what a leaver asks for.
            builder.HasIndex(l => new { l.CompanyId, l.EmployeeNo });

            builder.HasMany(l => l.BranchShares)
                   .WithOne(s => s.PayrollLine!)
                   .HasForeignKey(s => s.PayrollLineId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Ignore(l => l.GrossPay);
            builder.Ignore(l => l.NetPay);
        });

        modelBuilder.Entity<PayrollBranchShare>(builder =>
        {
            builder.ToTable("PayrollBranchShares", SchemaName);

            builder.Property(s => s.Amount).HasColumnType(DecimalPrecisionConventions.Money);

            // "What did staff cost this branch" reads this one.
            builder.HasIndex(s => new { s.CompanyId, s.BranchId });
        });

        modelBuilder.Entity<BranchAssignment>(builder =>
        {
            builder.ToTable("BranchAssignments", SchemaName);

            builder.Property(a => a.Reason).HasMaxLength(250);

            // Payroll asks "who was at this branch on this day" for every day of a month, so the
            // employee and the date together are what it seeks on.
            builder.HasIndex(a => new { a.EmployeeId, a.FromDate });
            builder.HasIndex(a => new { a.CompanyId, a.BranchId, a.FromDate });
        });
    }
}
