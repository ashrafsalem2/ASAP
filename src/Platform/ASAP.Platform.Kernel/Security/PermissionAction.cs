namespace ASAP.Platform.Kernel.Security;

/// <summary>
/// The verbs ASAP grants against a resource. Keeping the verb list closed, rather than letting
/// each module invent its own, is what makes the permission screen readable: an administrator
/// learns nine verbs once and then understands every module in the system.
/// </summary>
public enum PermissionAction
{
    /// <summary>See the records and run its reports.</summary>
    Read = 0,

    /// <summary>Add new records.</summary>
    Create = 1,

    /// <summary>Change existing records.</summary>
    Update = 2,

    /// <summary>Remove records. Never granted over posted ledger entries, which are corrected by reversal.</summary>
    Delete = 3,

    /// <summary>
    /// Commit a document to the ledgers. Deliberately separate from <see cref="Create"/>: a
    /// clerk who prepares journals is usually not the person allowed to post them.
    /// </summary>
    Post = 4,

    /// <summary>Approve a document that is waiting on someone else, such as a purchase order over a limit.</summary>
    Approve = 5,

    /// <summary>Reverse or cancel something already posted.</summary>
    Reverse = 6,

    /// <summary>Extract data out of ASAP, to a file or an integration.</summary>
    Export = 7,

    /// <summary>
    /// Push past a block ASAP raised, such as selling below cost. Always audited, and always
    /// granted separately from the ordinary verbs.
    /// </summary>
    Override = 8,

    /// <summary>Run an operation that is neither a read nor a write, such as a stock recalculation job.</summary>
    Execute = 9,
}
