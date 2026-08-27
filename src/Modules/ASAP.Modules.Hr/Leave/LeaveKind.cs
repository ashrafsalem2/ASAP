namespace ASAP.Modules.Hr.Leave;

/// <summary>Why somebody is away.</summary>
/// <remarks>
/// A closed list rather than free text, because what a leave is called decides whether it is paid,
/// at what fraction, and whether it comes off the annual balance. "Away" is not a category anybody
/// can compute a wage from.
/// </remarks>
public enum LeaveKind
{
    /// <summary>Ordinary paid annual leave, drawn from what has been accrued.</summary>
    Annual = 0,

    /// <summary>Illness. Paid on a sliding scale that falls away with length of absence.</summary>
    Sick = 1,

    /// <summary>Agreed time off with no pay.</summary>
    Unpaid = 2,

    /// <summary>Maternity leave.</summary>
    Maternity = 3,

    /// <summary>Pilgrimage leave, once in a period of service.</summary>
    Hajj = 4,

    /// <summary>Leave on marriage.</summary>
    Marriage = 5,

    /// <summary>Leave on a death in the family.</summary>
    Bereavement = 6,

    /// <summary>Leave to sit examinations while studying.</summary>
    Examination = 7,
}

/// <summary>Where a leave request has got to.</summary>
public enum LeaveStatus
{
    /// <summary>Being written. Nobody has been asked yet.</summary>
    Draft = 0,

    /// <summary>Asked for, and waiting on a decision.</summary>
    Submitted = 1,

    /// <summary>Granted. Counts against the balance and against pay.</summary>
    Approved = 2,

    /// <summary>Refused. Counts against nothing.</summary>
    Rejected = 3,

    /// <summary>Withdrawn, by whoever asked or by whoever granted it.</summary>
    Cancelled = 4,
}
