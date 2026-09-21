using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Content;

namespace SoftwareManagement.Domain.Portfolio;

/// <summary>
/// One public API the company offers (E-21).
///
/// The directory exists for developers deciding whether to build against something, so the two
/// questions it has to answer on the page itself are "where is it" and "which version should I
/// use" - not in a PDF behind a form.
/// </summary>
public class ApiCatalogEntry : AuditableEntity
{
    public Guid? ProductId { get; set; }

    public Product? Product { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    public ApiAuthScheme AuthScheme { get; set; } = ApiAuthScheme.ApiKey;

    public string? DocsUrl { get; set; }

    public string? OpenApiUrl { get; set; }

    public bool HasSandbox { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public DateTime? PublishedAtUtc { get; set; }

    public ICollection<ApiVersion> Versions { get; } = [];

    /// <summary>
    /// What is missing before this can be published. An entry with no versions tells a developer
    /// nothing they can act on (BR-API-01).
    /// </summary>
    public IReadOnlyList<string> PublishingShortfalls()
    {
        var shortfalls = new List<string>();

        if (Versions.Count == 0)
        {
            shortfalls.Add("at least one version");
        }
        else if (!Versions.Any(v => v.IsCurrent))
        {
            shortfalls.Add("one version marked current");
        }

        if (string.IsNullOrWhiteSpace(Purpose))
        {
            shortfalls.Add("a sentence saying what it is for");
        }

        return shortfalls;
    }
}

public enum ApiAuthScheme
{
    ApiKey = 0,
    OAuth2 = 1,
    JwtBearer = 2,
    None = 3,
}

/// <summary>
/// One version of an API (E-22).
///
/// Exactly one is current at a time, and a deprecated one cannot be it: telling a developer to
/// build against something being withdrawn is worse than telling them nothing (BR-API-02).
/// </summary>
public class ApiVersion : AuditableEntity
{
    /// <summary>
    /// How much notice a deprecation must give. Ninety days is the number in BR-API-03, and it is
    /// here so the rule and the check cannot drift apart: somebody integrating against a version
    /// needs a quarter to move off it.
    /// </summary>
    public const int MinimumSunsetNoticeDays = 90;

    public Guid ApiCatalogEntryId { get; set; }

    public ApiCatalogEntry? ApiCatalogEntry { get; set; }

    public string VersionLabel { get; set; } = string.Empty;

    public ApiVersionStatus Status { get; set; } = ApiVersionStatus.Stable;

    public DateOnly ReleasedOn { get; set; }

    public DateOnly? SunsetDate { get; set; }

    public string? ChangelogUrl { get; set; }

    public bool IsCurrent { get; set; }

    /// <summary>
    /// Whether a proposed sunset date gives enough notice, measured from the day the deprecation is
    /// applied rather than from the day the version was released.
    /// </summary>
    public static bool HasEnoughNotice(DateOnly? sunsetDate, DateOnly today) =>
        sunsetDate is { } date && date.DayNumber - today.DayNumber >= MinimumSunsetNoticeDays;
}

public enum ApiVersionStatus
{
    Beta = 0,
    Stable = 1,
    Deprecated = 2,
}
