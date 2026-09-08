using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Identity;

/// <summary>
/// A rotating refresh token (BR-IAM-03). Only the hash is stored, so a database dump cannot be
/// replayed as a session. Rotation leaves a chain: presenting a token that was already replaced
/// means it was stolen, and the whole chain is revoked.
/// </summary>
public class RefreshToken : AuditableEntity
{
    public Guid UserId { get; set; }

    public AdminUser? User { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    public string CreatedByIp { get; set; } = string.Empty;

    public string? RevokedReason { get; set; }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;

    /// <summary>
    /// True when the token was already rotated away. Presenting one of these is the signal that a
    /// token leaked, and it revokes the entire chain rather than just this row.
    /// </summary>
    public bool WasRotated => ReplacedByTokenHash is not null;
}
