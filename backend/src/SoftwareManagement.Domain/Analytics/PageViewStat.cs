using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Analytics;

/// <summary>
/// How many times a public path was viewed on one day.
///
/// This is the whole of the site's first-party analytics (A-27): a path, a date and four counters.
/// There is no cookie, no visitor identifier and nothing that could be joined back to a person, so
/// no consent banner is owed for it and nothing here is personal data under the DPDP Act.
/// </summary>
public class PageViewStat : AuditableEntity
{
    /// <summary>The public path, for example <c>/products/hospital-management</c>.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The day the views fell on, in the company's display timezone rather than UTC.</summary>
    public DateOnly StatDate { get; set; }

    public int Views { get; set; }

    /// <summary>
    /// Counted from nothing that identifies anyone: it rises only when a request arrives without a
    /// referrer from this site, which approximates a new arrival. It is deliberately an
    /// approximation, because the accurate version would need to track people.
    /// </summary>
    public int UniqueVisitors { get; set; }

    public int FormStarts { get; set; }

    public int FormSubmits { get; set; }
}
