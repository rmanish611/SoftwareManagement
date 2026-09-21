using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Crm;

namespace SoftwareManagement.Domain.Leads;

/// <summary>
/// Someone who asked the company a question. Every accepted enquiry becomes exactly one of these,
/// in stage <see cref="LeadStage.New"/>, owned by whoever the settings name (BR-LEAD-07).
/// </summary>
public class Lead : AuditableEntity
{
    /// <summary>
    /// A contactable enquiry needs an email or a phone number. Both are nullable individually,
    /// because a visitor may leave either, and the database rejects a row with neither
    /// (BR-LEAD-01).
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? CompanyName { get; set; }

    public string? Message { get; set; }

    public Guid? ProductId { get; set; }

    public Product? Product { get; set; }

    public LeadStage Stage { get; set; } = LeadStage.New;

    public string? DisqualifyReason { get; set; }

    /// <summary>
    /// Where the enquiry came from. Recorded as <see cref="LeadSource.Direct"/> rather than left
    /// empty when nothing identifies a campaign, because "we do not know" and "they typed the
    /// address" are different answers and a report that conflates them is misleading (REQ-LEAD-008).
    /// </summary>
    public LeadSource Source { get; set; } = LeadSource.Direct;

    /// <summary>The campaign parameters as they arrived, kept verbatim for the traffic report.</summary>
    public string? UtmJson { get; set; }

    public Guid? OwnerUserId { get; set; }

    public DateTime? FirstResponseAtUtc { get; set; }

    /// <summary>
    /// When a first reply is owed. Counted in business hours, so an enquiry arriving on Saturday
    /// evening is not already late on Monday morning (NFR-SLA, BR-LEAD-12).
    /// </summary>
    public DateTime SlaDueAtUtc { get; set; }

    public Guid? MergedIntoLeadId { get; set; }

    /// <summary>
    /// The customer record this lead became, set by converting it (REQ-LEAD-015). Both are null
    /// until then, and a converted lead has both: a company with nobody to ring is not a customer.
    /// </summary>
    public Guid? OrganisationId { get; set; }

    public Organisation? Organisation { get; set; }

    public Guid? ContactId { get; set; }

    public Contact? Contact { get; set; }

    /// <summary>
    /// When the last reminder about this lead going cold was queued, so the 30-day nudge is sent
    /// once rather than every time the sweep runs (BR-LEAD-11).
    /// </summary>
    public DateTime? StaleReminderSentAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    public ICollection<FormSubmission> Submissions { get; } = [];

    public ICollection<LeadActivity> Activities { get; } = [];

    /// <summary>
    /// Whether this lead belongs in the funnel at all. Spam is excluded permanently (BR-LEAD-12)
    /// and a merged record is counted under its survivor (BR-LEAD-08); counting either would
    /// inflate the denominator of every conversion figure the owner looks at.
    /// </summary>
    public bool CountsTowardsReporting() =>
        !IsDeleted && Stage != LeadStage.Spam && Stage != LeadStage.Merged;
}

public enum LeadStage
{
    New = 0,
    Contacted = 1,
    Qualified = 2,
    Converted = 3,
    Disqualified = 4,
    Nurturing = 5,
    Spam = 6,
    Merged = 7,
}

public enum LeadSource
{
    /// <summary>Nothing identified a campaign: they arrived at the site and asked.</summary>
    Direct = 0,
    Organic = 1,
    Referral = 2,
    Campaign = 3,
    Social = 4,
    Email = 5,
}
