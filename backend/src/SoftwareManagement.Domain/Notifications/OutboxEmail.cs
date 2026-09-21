using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Notifications;

/// <summary>
/// A message waiting to be sent.
///
/// Nothing is ever sent during a web request. The message is written to this table inside the same
/// transaction as the business change that caused it, so either both happened or neither did: a
/// lead can never exist without its acknowledgement queued, and an acknowledgement can never go out
/// for a lead that was rolled back (BR-NOTIF-01).
/// </summary>
public class OutboxEmail : AuditableEntity
{
    /// <summary>
    /// The retry schedule in minutes, from BR-NOTIF-02. Five attempts, then the message is
    /// dead-lettered and a person is told, because silently giving up on an enquiry is the failure
    /// this whole table exists to prevent.
    /// </summary>
    public static readonly int[] RetryDelaysMinutes = [1, 5, 15, 60, 240];

    public static int MaxAttempts => RetryDelaysMinutes.Length;

    public string TemplateKey { get; set; } = string.Empty;

    public string ToAddress { get; set; } = string.Empty;

    public string? CcAddress { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string HtmlBody { get; set; } = string.Empty;

    public string TextBody { get; set; } = string.Empty;

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }

    public string? LastError { get; set; }

    public Guid CorrelationId { get; set; }

    public string? RelatedEntityType { get; set; }

    public Guid? RelatedEntityId { get; set; }

    public ICollection<EmailDeliveryLog> DeliveryLogs { get; } = [];

    /// <summary>
    /// When to try again after a failure, or null when the attempts are exhausted. The caller uses
    /// null to mean dead-letter, so the schedule and the giving-up rule are the same decision.
    /// </summary>
    public static DateTime? NextAttemptAfter(int attemptsSoFar, DateTime nowUtc) =>
        attemptsSoFar >= RetryDelaysMinutes.Length
            ? null
            : nowUtc.AddMinutes(RetryDelaysMinutes[attemptsSoFar]);
}

public enum OutboxStatus
{
    Pending = 0,
    Sending = 1,
    Sent = 2,
    DeadLettered = 3,
    Cancelled = 4,
}

/// <summary>
/// What the mail server actually said, on each attempt. Kept so the owner never has to wonder
/// whether a message went out (REQ-NOTIF-005). Append-only.
/// </summary>
public class EmailDeliveryLog
{
    public Guid Id { get; set; }

    public Guid OutboxEmailId { get; set; }

    public OutboxEmail? OutboxEmail { get; set; }

    public int AttemptNumber { get; set; }

    public DateTime AttemptedAtUtc { get; set; }

    public string? SmtpStatusCode { get; set; }

    public string? SmtpResponse { get; set; }

    public bool Succeeded { get; set; }

    public int DurationMs { get; set; }
}

/// <summary>
/// The wording of a message, editable without a deployment.
///
/// Placeholders are declared rather than inferred, so composing can refuse a message with a
/// placeholder nobody supplied instead of sending "Dear {{name}}" to a customer (REQ-NOTIF-003).
/// </summary>
public class EmailTemplate : AuditableEntity
{
    public const string LeadAcknowledgement = "lead.acknowledgement";
    public const string LeadOwnerAlert = "lead.owner-alert";
    public const string OutboxDeadLettered = "outbox.dead-lettered";
    public const string LeadFollowUpDue = "lead.follow-up-due";
    public const string LeadGoneCold = "lead.gone-cold";
    public const string InvoiceOverdue = "invoice.overdue";
    public const string SubscriptionRenewing = "subscription.renewing";

    public string Key { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string HtmlBody { get; set; } = string.Empty;

    public string TextBody { get; set; } = string.Empty;

    /// <summary>The placeholder names this template is allowed to use, as a JSON array.</summary>
    public string PlaceholdersJson { get; set; } = "[]";

    public int Version { get; set; } = 1;

    public bool IsActive { get; set; } = true;
}
