using Microsoft.AspNetCore.Identity;

namespace SoftwareManagement.Domain.Identity;

/// <summary>
/// A person who can sign in to the back office. There is no public customer login in this system
/// (A-12): visitors are leads, not accounts.
/// </summary>
public class AdminUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// A deactivated user keeps their history and their audit rows but can no longer sign in, and
    /// their refresh tokens are revoked the moment the flag is cleared (REQ-IAM-007).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Windows time zone id used to render every instant for this person (A-08).</summary>
    public string DisplayTimeZone { get; set; } = "India Standard Time";

    public DateTime? LastLoginAtUtc { get; set; }

    public bool NotifyOnNewLead { get; set; } = true;

    public bool NotifyDailyDigest { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }
}
