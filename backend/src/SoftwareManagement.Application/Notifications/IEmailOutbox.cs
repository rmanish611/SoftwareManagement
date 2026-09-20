using SoftwareManagement.Domain.Notifications;

namespace SoftwareManagement.Application.Notifications;

/// <summary>
/// Puts a message in the outbox. It never sends: sending happens later, in the pump, so a slow or
/// dead mail server can never make a visitor's form submission fail (BR-NOTIF-01).
/// </summary>
public interface IEmailOutbox
{
    /// <summary>
    /// Composes a message from its template and adds it to the current unit of work. The caller
    /// saves, so the message is committed by the same transaction as whatever caused it.
    /// </summary>
    Task<QueueResult> QueueAsync(
        string templateKey,
        string toAddress,
        IReadOnlyDictionary<string, string> values,
        Guid correlationId,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null,
        CancellationToken cancellationToken = default);
}

public sealed record QueueResult(bool Queued, string Reason, OutboxEmail? Message)
{
    public static QueueResult Ok(OutboxEmail message) => new(true, "queued", message);

    public static QueueResult Refused(string reason) => new(false, reason, null);
}

/// <summary>
/// Turns a template and a set of values into a subject and a body.
///
/// A message is composed completely or not at all. A template that still holds an unfilled
/// placeholder is refused rather than sent, because "Dear {{fullName}}" reaching a customer is
/// worse than a message that did not go out and was logged (REQ-NOTIF-003).
/// </summary>
public interface IEmailComposer
{
    ComposedEmail Compose(EmailTemplate emailTemplate, IReadOnlyDictionary<string, string> values);
}

public sealed record ComposedEmail(bool Success, string Subject, string HtmlBody, string TextBody, string? Problem)
{
    public static ComposedEmail Ok(string subject, string html, string text) => new(true, subject, html, text, null);

    public static ComposedEmail Failed(string problem) => new(false, string.Empty, string.Empty, string.Empty, problem);
}

/// <summary>Sends one message. The only part that talks to a mail server.</summary>
public interface IEmailSender
{
    Task<SendOutcome> SendAsync(OutboxEmail message, CancellationToken cancellationToken);
}

public sealed record SendOutcome(bool Succeeded, string? StatusCode, string? Response, int DurationMs);

/// <summary>
/// Drains the outbox: takes what is due, sends it, records what the server said, and reschedules or
/// dead-letters what failed. Exposed as an interface so a test can run one pass deterministically
/// instead of waiting for a timer.
/// </summary>
public interface IOutboxPump
{
    Task<OutboxPassResult> RunOnceAsync(CancellationToken cancellationToken);
}

public sealed record OutboxPassResult(int Considered, int Sent, int Rescheduled, int DeadLettered);
