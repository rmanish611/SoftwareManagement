using SoftwareManagement.Domain.Leads;

namespace SoftwareManagement.Application.Leads;

/// <summary>
/// Everything that happens when a stranger presses Send on a public form.
///
/// It is one service rather than a controller full of steps because the steps are not independent:
/// the submission, the consent record, the lead and the two queued emails are one fact about the
/// world, and a half-applied version of it is worse than a rejection. They are written in a single
/// transaction (REQ-LEAD-001, BR-NOTIF-01).
/// </summary>
public interface ILeadIntakeService
{
    Task<IntakeResult> SubmitAsync(IntakeRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// One submission, as it arrived. The network facts are separated from the answers because they are
/// gathered differently and trusted differently: the answers came from the visitor, the address and
/// the user agent came from the connection.
/// </summary>
public sealed record IntakeRequest(
    string FormKey,
    IReadOnlyDictionary<string, string?> Answers,
    bool Consent,
    string? CaptchaToken,

    /// <summary>The hidden field no person can see. Anything in it came from a bot (BR-LEAD-03).</summary>
    string? HoneypotValue,
    string IpAddress,
    string? UserAgent,
    string? Referrer,
    IReadOnlyDictionary<string, string> Utm);

public sealed record IntakeResult(
    IntakeOutcome Outcome,
    Guid? LeadId,
    string? Reference,
    string? Message,

    /// <summary>The field that failed, when one did, so the form can point at it.</summary>
    string? Field = null,

    /// <summary>How long the caller should wait, for a rate-limited answer.</summary>
    int? RetryAfterSeconds = null)
{
    public static IntakeResult Accepted(Guid leadId, string reference, string message) =>
        new(IntakeOutcome.Accepted, leadId, reference, message);

    /// <summary>
    /// A duplicate is a success from the visitor's side: they pressed the button twice and their
    /// enquiry did arrive. They are shown the same reference as the first time (BR-LEAD-05).
    /// </summary>
    public static IntakeResult Duplicate(Guid leadId, string reference, string message) =>
        new(IntakeOutcome.Duplicate, leadId, reference, message);

    /// <summary>
    /// A bot gets the same answer a person does, so it learns nothing about what caught it
    /// (BR-LEAD-03).
    /// </summary>
    public static IntakeResult Spam(string reference, string message) =>
        new(IntakeOutcome.SilentlyDiscarded, null, reference, message);

    public static IntakeResult Invalid(string message, string? field = null) =>
        new(IntakeOutcome.Invalid, null, null, message, field);

    public static IntakeResult Failed(IntakeOutcome outcome, string message, int? retryAfterSeconds = null) =>
        new(outcome, null, null, message, null, retryAfterSeconds);
}

public enum IntakeOutcome
{
    Accepted = 0,
    Duplicate = 1,

    /// <summary>Stored as spam and answered normally. The caller must not reveal this.</summary>
    SilentlyDiscarded = 2,
    Invalid = 3,
    ConsentRequired = 4,
    CaptchaInvalid = 5,
    RateLimited = 6,
    FormNotFound = 7,
    FormClosed = 8,
}

/// <summary>
/// Verifies that a submission came from a person.
///
/// The check is server-side, and a token is good once: a token that has already been spent, or one
/// older than its lifetime, is refused (BR-LEAD-02). A client-side check would stop nothing.
/// </summary>
public interface ICaptchaVerifier
{
    /// <summary>The maximum age of a token, in seconds. Turnstile's own limit.</summary>
    public const int MaxTokenAgeSeconds = 300;

    Task<CaptchaVerification> VerifyAsync(string? token, string ipAddress, CancellationToken cancellationToken);
}

public sealed record CaptchaVerification(bool Passed, string Reason)
{
    public static CaptchaVerification Ok() => new(true, "verified");

    public static CaptchaVerification Rejected(string reason) => new(false, reason);
}

/// <summary>
/// How many submissions one address has made lately (BR-LEAD-04). Counted from the submissions
/// table rather than from memory, so a restart does not hand a flood a fresh allowance.
/// </summary>
public interface ISubmissionRateLimiter
{
    Task<RateLimitDecision> CheckAsync(string ipAddress, CancellationToken cancellationToken);
}

public sealed record RateLimitDecision(bool Allowed, int RetryAfterSeconds)
{
    public static RateLimitDecision Allow() => new(true, 0);

    public static RateLimitDecision Deny(int retryAfterSeconds) => new(false, retryAfterSeconds);
}

/// <summary>
/// Where an enquiry is due a first reply. Counted in the company's business hours, so a form sent
/// at 23:00 on a Saturday is not already overdue when the office opens.
/// </summary>
public interface ISlaCalculator
{
    DateTime FirstResponseDueUtc(DateTime receivedUtc);

    /// <summary>
    /// How much working time passed between two instants, which is what the response-time report
    /// means by "answered in two hours" (BR-LEAD-10).
    ///
    /// Counting wall-clock hours would make every enquiry that arrived on a Friday evening look
    /// like a two-day failure, and the number nobody believes is the number nobody acts on.
    /// </summary>
    TimeSpan BusinessTimeBetween(DateTime fromUtc, DateTime toUtc);
}

/// <summary>
/// The days the office is shut, as the SLA clock needs them: already resolved for a given year, so
/// the calculator does not have to know that some holidays recur and others move.
/// </summary>
public interface IHolidayCalendar
{
    bool IsHoliday(DateOnly localDate);
}
