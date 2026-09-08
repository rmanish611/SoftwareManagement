namespace SoftwareManagement.Domain.Audit;

/// <summary>
/// Append-only record of every mutating admin action (BR-ADM-01). Deliberately not an
/// <c>AuditableEntity</c>: an audit row is never modified, so it has no modification stamp and no
/// concurrency token. There is no update or delete path in the API or the user interface.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public Guid? ActorUserId { get; set; }

    public string? ActorEmail { get; set; }

    public string? ActorIp { get; set; }

    /// <summary>Created, Updated, Deleted, SignedIn, RoleAssigned and so on.</summary>
    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    /// <summary>
    /// Values before and after the change, with sensitive fields redacted to `***` before they are
    /// serialised (NFR-PRIV-03). Null on a create and on a delete respectively.
    /// </summary>
    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }

    public Guid CorrelationId { get; set; }
}

/// <summary>Action names used in the audit trail. Stable strings: reports group by them.</summary>
public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
    public const string SignedIn = "SignedIn";
    public const string SignedOut = "SignedOut";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordReset = "PasswordReset";
    public const string RoleAssigned = "RoleAssigned";
    public const string UserDeactivated = "UserDeactivated";
    public const string TokenChainRevoked = "TokenChainRevoked";
}
