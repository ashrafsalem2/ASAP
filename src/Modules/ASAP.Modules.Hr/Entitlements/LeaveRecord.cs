using ASAP.Modules.Hr.People;
using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Hr.Entitlements;

/// <summary>
/// One period of leave somebody actually took.
/// </summary>
/// <remarks>
/// Before this existed, <see cref="People.EmployeeService.EntitlementsAsync"/> computed everybody's
/// leave balance as though nobody had ever taken a day -- said plainly in its own remarks as an
/// upper bound rather than a fact. This is the register that turns it into one: what somebody
/// earns is a calculation, what they took is a record, and the balance is only ever the two of
/// them together.
/// </remarks>
public sealed class LeaveRecord : CompanyEntity
{
    /// <summary>Who took the leave.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>The employee, when loaded.</summary>
    public Employee? Employee { get; set; }

    /// <summary>The first day away.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>The last day away.</summary>
    public DateOnly ToDate { get; set; }

    /// <summary>What it was for, when that is worth recording.</summary>
    public string? Note { get; set; }

    /// <summary>How many days this covers, both ends included.</summary>
    public decimal Days => ToDate.DayNumber - FromDate.DayNumber + 1;
}
