namespace UserManagement.Domain.Enums;

/// <summary>
/// Why a refresh token stopped being usable. Turns "why is this session gone?" from a support guess into a
/// query. Stored as <c>tinyint</c> with a check constraint; values are part of the database contract.
/// </summary>
public enum RevocationReason : byte
{
    /// <summary>Replaced by a successor during normal rotation.</summary>
    Rotated = 0,

    /// <summary>
    /// An already-rotated token was presented, meaning two clients hold tokens from one lineage. The whole
    /// family is revoked with this reason and the event is logged at Warning.
    /// </summary>
    ReuseDetected = 1,

    Logout = 2,

    PasswordChanged = 3,

    RoleChanged = 4,

    UserDeleted = 5,

    Expired = 6,
}
