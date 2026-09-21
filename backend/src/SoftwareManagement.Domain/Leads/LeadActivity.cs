using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Leads;

/// <summary>
/// One thing that happened to a lead: a note, a call, an email either way, a meeting, a stage
/// change, or something the system did on its own.
///
/// Append-only. The history of a conversation is the one thing a salesperson cannot reconstruct
/// from memory six months later, so nothing here can be edited or deleted after the fact; the only
/// field that changes is <see cref="IsCompleted"/> on a follow-up reminder, which is the difference
/// between "still owed" and "done" rather than a revision of what was written (E-31, BR-LEAD-09).
/// </summary>
public class LeadActivity : AuditableEntity
{
    public Guid LeadId { get; set; }

    public Lead? Lead { get; set; }

    public LeadActivityType ActivityType { get; set; }

    public LeadActivityDirection Direction { get; set; }

    public string? Body { get; set; }

    /// <summary>
    /// When the thing happened, which is not always when it was typed. Somebody logging Friday's
    /// call on Monday morning is recording Friday, and the SLA clock has to believe them.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; }

    public LeadStage? FromStage { get; set; }

    public LeadStage? ToStage { get; set; }

    /// <summary>Set on a follow-up reminder: when the salesperson asked to be reminded.</summary>
    public DateTime? DueAtUtc { get; set; }

    public bool IsCompleted { get; set; }

    /// <summary>
    /// Whether this activity stops the first-response clock.
    ///
    /// Only something that actually went to the enquirer counts (BR-LEAD-10). A private note saying
    /// "will call them tomorrow" is work, but it is not an answer, and counting it would make the
    /// response-time report flattering and useless.
    /// </summary>
    public bool IsFirstResponseCandidate() =>
        Direction == LeadActivityDirection.Outbound
        && ActivityType is LeadActivityType.Call or LeadActivityType.EmailOut or LeadActivityType.Meeting;
}

public enum LeadActivityType
{
    Note = 0,
    Call = 1,
    EmailOut = 2,
    EmailIn = 3,
    Meeting = 4,
    StageChange = 5,
    SystemEvent = 6,
}

public enum LeadActivityDirection
{
    Inbound = 0,
    Outbound = 1,
    Internal = 2,
}
