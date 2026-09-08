using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Identity;

/// <summary>
/// Every sign-in attempt, successful or not. This is both the lockout counter's source of truth
/// (BR-IAM-02) and the record the Auditor reads when investigating access (REQ-IAM-011).
/// Append-only: there is no update or delete path.
/// </summary>
public class LoginAttempt : AuditableEntity
{
    public string EmailAttempted { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    public string? UserAgent { get; set; }

    /// <summary>
    /// A short code, never a sentence that distinguishes "no such user" from "wrong password":
    /// the response to the caller is identical in both cases (BR-IAM-02).
    /// </summary>
    public string? FailureReason { get; set; }
}

/// <summary>Failure codes recorded against an attempt. Never returned to an anonymous caller.</summary>
public static class LoginFailureReasons
{
    public const string UnknownUser = "unknown_user";
    public const string WrongPassword = "wrong_password";
    public const string LockedOut = "locked_out";
    public const string Inactive = "inactive";
    public const string TwoFactorRequired = "two_factor_required";
    public const string TwoFactorInvalid = "two_factor_invalid";
}
