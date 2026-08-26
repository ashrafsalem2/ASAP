namespace ASAP.Platform.Kernel.Messaging;

/// <summary>
/// How much weight a message carries. The distinction between <see cref="Error"/> and
/// <see cref="Blocked"/> matters: an error means the request was wrong and the user can retry
/// after fixing it, while blocked means ASAP is deliberately refusing a request that is
/// well-formed but would damage the books — an offer that sells below cost, a posting into a
/// closed period. Blocked messages always carry a resolution and, where the setup permits,
/// the name of the override permission that would allow it.
/// </summary>
public enum MessageSeverity
{
    /// <summary>Neutral information. Never stops anything.</summary>
    Information = 0,

    /// <summary>An operation completed. Used for posting confirmations carrying the entry numbers.</summary>
    Success = 1,

    /// <summary>Something the user should know about, but the operation still went through.</summary>
    Warning = 2,

    /// <summary>The request was invalid. Fix the input and try again.</summary>
    Error = 3,

    /// <summary>
    /// The request was valid but ASAP refused it to protect data integrity. Always carries a
    /// reason and a resolution, and names an override permission when one exists.
    /// </summary>
    Blocked = 4,
}
