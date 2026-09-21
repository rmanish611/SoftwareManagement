using System.Text.Json;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Domain.Crm;

namespace SoftwareManagement.Domain.Portfolio;

/// <summary>
/// Something the company delivered (E-23).
///
/// The client's name is held separately from the client's permission to use it, because those are
/// two different facts and the second one changes. A project whose customer has not agreed to be
/// named is still worth showing - as "a leading hospital group" - and that is what the public page
/// renders (BR-PRJ-01).
/// </summary>
public class Project : AuditableEntity
{
    public string Title { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public Guid? OrganisationId { get; set; }

    public Organisation? Organisation { get; set; }

    /// <summary>What to call the client, when they have said we may say it.</summary>
    public string? ClientDisplayName { get; set; }

    public string Industry { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public DateOnly StartedOn { get; set; }

    public DateOnly? CompletedOn { get; set; }

    public Guid? ProductId { get; set; }

    public Product? Product { get; set; }

    public Guid? CoverAssetId { get; set; }

    public bool IsFeatured { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public DateTime? PublishedAtUtc { get; set; }

    public Guid? ClientLogoId { get; set; }

    public ClientLogo? ClientLogo { get; set; }

    public CaseStudy? CaseStudy { get; set; }

    /// <summary>
    /// What this was built with. The stack page claims the company knows these things; this is the
    /// evidence behind the claim, which is why the link is recorded on the project rather than
    /// written out again on the page (REQ-PRJ-008).
    /// </summary>
    public ICollection<ProjectTechnology> Technologies { get; } = [];

    /// <summary>
    /// What a visitor is allowed to be told about who this was for.
    ///
    /// The anonymised label is not a fallback for missing data - it is the correct answer when a
    /// client has not given permission, and the only one that keeps a promise made to them
    /// (BR-PRJ-01).
    /// </summary>
    public string PublicClientLabel()
    {
        if (ClientLogo?.HasPermission == true && !string.IsNullOrWhiteSpace(ClientLogo.DisplayName))
        {
            return ClientLogo.DisplayName;
        }

        return !string.IsNullOrWhiteSpace(ClientDisplayName) && ClientLogo is null
            ? ClientDisplayName
            : $"a leading {Industry.ToLowerInvariant()} company";
    }
}

/// <summary>
/// The story behind a project (E-24).
///
/// The metrics are the part that makes it a case study rather than a brochure, so they are required
/// before it can be published: a page that says "significantly improved" and nothing else is a page
/// nobody believes (BR-PRJ-02).
/// </summary>
public class CaseStudy : AuditableEntity
{
    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public string Problem { get; set; } = string.Empty;

    public string Approach { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    /// <summary>An array of `{label, value, unit}`, at least one entry before publishing.</summary>
    public string MetricsJson { get; set; } = "[]";

    public Guid? TestimonialId { get; set; }

    public Testimonial? Testimonial { get; set; }

    /// <summary>
    /// The metrics as objects. A malformed document reads as none rather than throwing, because the
    /// publishing check is the place that refuses it and a reader should not crash on it.
    /// </summary>
    public IReadOnlyList<OutcomeMetric> Metrics()
    {
        if (string.IsNullOrWhiteSpace(MetricsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<OutcomeMetric>>(MetricsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Whether this is ready to be published, and what is short if not.
    ///
    /// A number with no unit is not a measurement: "reduced by 40" is a claim, "reduced by 40
    /// percent" is a fact (BR-PRJ-02).
    /// </summary>
    public IReadOnlyList<string> PublishingShortfalls()
    {
        var shortfalls = new List<string>();

        if (string.IsNullOrWhiteSpace(Problem))
        {
            shortfalls.Add("the problem");
        }

        if (string.IsNullOrWhiteSpace(Approach))
        {
            shortfalls.Add("the approach");
        }

        if (string.IsNullOrWhiteSpace(Outcome))
        {
            shortfalls.Add("the outcome");
        }

        var metrics = Metrics();

        if (metrics.Count == 0)
        {
            shortfalls.Add("at least one outcome metric with a number and a unit");
        }
        else if (metrics.Any(m => string.IsNullOrWhiteSpace(m.Label) || string.IsNullOrWhiteSpace(m.Unit)))
        {
            shortfalls.Add("a label and a unit on every metric");
        }

        return shortfalls;
    }
}

/// <summary>One measured result: "response time", 40, "percent faster".</summary>
public sealed record OutcomeMetric(string Label, decimal Value, string Unit);

/// <summary>
/// A client's mark, and whether we may show it (E-25).
///
/// `HasPermission` defaults to false, which is the only safe default: a logo used without agreement
/// is a promise broken in public.
/// </summary>
public class ClientLogo : AuditableEntity
{
    public Guid? OrganisationId { get; set; }

    public Organisation? Organisation { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public Guid MediaAssetId { get; set; }

    public MediaAsset? MediaAsset { get; set; }

    public bool HasPermission { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// A project and one of the technologies it used.
///
/// The data model draws this as a many-to-many with no attributes of its own, so the join carries
/// nothing but the two keys, and a composite primary key stops the same pairing being recorded
/// twice (ASM-23).
/// </summary>
public class ProjectTechnology
{
    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public Guid TechnologyId { get; set; }

    public Technology? Technology { get; set; }
}
