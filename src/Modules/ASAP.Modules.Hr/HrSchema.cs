using ASAP.Modules.Hr.Entitlements;
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

        modelBuilder.Entity<Attendance.Shift>(builder =>

        {

            builder.ToTable("Shifts", SchemaName);


            builder.Property(s => s.Code).HasMaxLength(20).IsRequired();

            builder.Property(s => s.Name).HasMaxLength(120).IsRequired();

            builder.Property(s => s.NameArabic).HasMaxLength(120);

            builder.Property(s => s.RowVersion).IsRowVersion();


            builder.HasIndex(s => new { s.CompanyId, s.Code })

                   .IsUnique()

                   .HasFilter("[IsDeleted] = 0");

        });


        modelBuilder.Entity<Attendance.ShiftAssignment>(builder =>

        {

            builder.ToTable("ShiftAssignments", SchemaName);


            builder.Property(a => a.EmployeeNo).HasMaxLength(20).IsRequired();

            builder.Property(a => a.ShiftCode).HasMaxLength(20).IsRequired();

            builder.Property(a => a.RowVersion).IsRowVersion();


            builder.HasOne<Employee>()

                   .WithMany()

                   .HasForeignKey(a => a.EmployeeId)

                   .OnDelete(DeleteBehavior.Restrict);


            builder.HasIndex(a => new { a.CompanyId, a.EmployeeId, a.FromDate });

        });


        modelBuilder.Entity<Attendance.AttendanceRecord>(builder =>

        {

            builder.ToTable("AttendanceRecords", SchemaName);


            builder.Property(a => a.EmployeeNo).HasMaxLength(20).IsRequired();

            builder.Property(a => a.ShiftCode).HasMaxLength(20);

            builder.Property(a => a.Note).HasMaxLength(500);

            builder.Property(a => a.RecordedByUserName).HasMaxLength(120);

            builder.Property(a => a.RowVersion).IsRowVersion();


            builder.HasOne<Employee>()

                   .WithMany()

                   .HasForeignKey(a => a.EmployeeId)

                   .OnDelete(DeleteBehavior.Restrict);


            // One account of one day. Two would be added together by every figure derived from them.

            builder.HasIndex(a => new { a.CompanyId, a.EmployeeId, a.OnDate })

                   .IsUnique()

                   .HasFilter("[IsDeleted] = 0");

        });


        modelBuilder.Entity<EmploymentContract>(builder =>

        {

            builder.ToTable("EmploymentContracts", SchemaName);


            builder.Property(c => c.EmployeeNo).HasMaxLength(20).IsRequired();

            builder.Property(c => c.Reference).HasMaxLength(60);

            builder.Property(c => c.RecordedByUserName).HasMaxLength(120);

            builder.Property(c => c.Reason).HasMaxLength(500);

            builder.Property(c => c.BasicWage).HasPrecision(18, 2);

            builder.Property(c => c.Allowances).HasPrecision(18, 2);

            builder.Property(c => c.RowVersion).IsRowVersion();


            builder.HasOne<Employee>()

                   .WithMany()

                   .HasForeignKey(c => c.EmployeeId)

                   .OnDelete(DeleteBehavior.Restrict);


            // Every payroll run reads along this, one query for the whole period.

            builder.HasIndex(c => new { c.CompanyId, c.EmployeeId, c.StartsOn });

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

        modelBuilder.Entity<Leave.LeaveRequest>(builder =>
        {
            builder.ToTable("LeaveRequests", SchemaName);

            builder.Property(r => r.No).HasMaxLength(20).IsRequired();
            builder.Property(r => r.EmployeeNo).HasMaxLength(20).IsRequired();
            builder.Property(r => r.EmployeeName).HasMaxLength(200).IsRequired();
            builder.Property(r => r.Reason).HasMaxLength(500);
            builder.Property(r => r.DecisionNote).HasMaxLength(500);

            builder.HasIndex(r => new { r.CompanyId, r.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // Payroll and the liability report both ask "what did this person have between these
            // dates", which is what this is seeked on.
            builder.HasIndex(r => new { r.EmployeeId, r.FromDate });

            builder.Ignore(r => r.Days);
            builder.Ignore(r => r.Counts);
            builder.Ignore(r => r.IsEditable);
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

        modelBuilder.Entity<EntitlementProvision>(builder =>
        {
            builder.ToTable("EntitlementProvisions", SchemaName);

            builder.Property(p => p.PostedAmount).HasColumnType(DecimalPrecisionConventions.Money);

            // One running row per company per provision. Never two, or the next run would not
            // know which figure it was measured against.
            builder.HasIndex(p => new { p.CompanyId, p.Type })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");
        });
    }
}
