using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Portfolio;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Domain.Portfolio;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// The public API catalogue: the admin side.
/// </summary>
[ApiController]
[Route("api/v1/admin/api-catalog")]
public sealed class ApiCatalogController(AppDbContext dbContext, IPortfolioService portfolio, IClock clock) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IPortfolioService _portfolio = portfolio;
    private readonly IClock _clock = clock;

    [HttpGet]
    [Authorize(Policy = Permissions.Catalog.ApiRead)]
    public async Task<ActionResult<IReadOnlyList<ApiEntrySummary>>> List(CancellationToken cancellationToken)
    {
        var entries = await _dbContext.ApiCatalogEntries
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new ApiEntrySummary(
                a.Id,
                a.Name,
                a.Slug,
                a.Status.ToString(),
                a.AuthScheme.ToString(),
                a.BaseUrl,
                a.Versions.Count,
                a.Versions.Where(v => v.IsCurrent).Select(v => v.VersionLabel).FirstOrDefault()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(entries);
    }

    /// <summary>
    /// Deprecations coming up, so none is forgotten.
    ///
    /// An empty result is an answer, not a blank table: the response says which window was asked
    /// about and says in words that nothing falls inside it, so the page has something to render
    /// (REQ-API-008).
    /// </summary>
    [HttpGet("sunsets")]
    [Authorize(Policy = Permissions.Catalog.ApiRead)]
    public async Task<ActionResult<SunsetReport>> Sunsets(
        [FromQuery] int withinDays = 30,
        CancellationToken cancellationToken = default)
    {
        var window = Math.Clamp(withinDays, 1, 365);
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var limit = today.AddDays(window);

        var rows = await _dbContext.ApiVersions
            .AsNoTracking()
            .Where(v => v.Status == ApiVersionStatus.Deprecated && v.SunsetDate != null && v.SunsetDate <= limit)
            .OrderBy(v => v.SunsetDate)
            .Select(v => new SunsetRow(
                v.ApiCatalogEntry!.Name,
                v.ApiCatalogEntry.Slug,
                v.VersionLabel,
                v.SunsetDate!.Value,
                v.SunsetDate.Value.DayNumber - today.DayNumber))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new SunsetReport(
            window,
            today,
            rows,
            rows.Count == 0 ? $"No API version is being withdrawn in the next {window} days." : null));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.ApiWrite)]
    public async Task<ActionResult<ApiEntrySummary>> Create(ApiEntryBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body.Name))
        {
            return Refuse("NAME_REQUIRED", "name", "An API needs a name a developer would recognise.");
        }

        if (string.IsNullOrWhiteSpace(body.Purpose))
        {
            return Refuse("PURPOSE_REQUIRED", "purpose", "Say in a sentence what it is for. The directory is read by people deciding whether to build against it.");
        }

        var slug = Slug.From(string.IsNullOrWhiteSpace(body.Slug) ? body.Name : body.Slug);

        if (!Slug.IsValid(slug))
        {
            return Refuse("INVALID_SLUG", "slug", $"A slug is {Slug.MinLength} to {Slug.MaxLength} characters, lowercase letters, digits and single hyphens.");
        }

        if (await _dbContext.ApiCatalogEntries.AnyAsync(a => a.Slug == slug, cancellationToken).ConfigureAwait(false))
        {
            return Problem(
                title: "That address is taken",
                detail: $"Another API entry already uses \"{slug}\".",
                statusCode: StatusCodes.Status409Conflict,
                type: PortfolioErrors.Type("SLUG_TAKEN"),
                extensions: PortfolioErrors.Extensions("SLUG_TAKEN", "slug", null));
        }

        if (!Enum.TryParse<ApiAuthScheme>(body.AuthScheme, ignoreCase: true, out var scheme))
        {
            return Refuse("UNKNOWN_AUTH_SCHEME", "authScheme", "The auth scheme must be an API key, OAuth2, a JWT bearer token, or none.");
        }

        var entry = new ApiCatalogEntry
        {
            Id = Guid.NewGuid(),
            ProductId = body.ProductId,
            Name = body.Name.Trim(),
            Slug = slug,
            Purpose = body.Purpose.Trim(),
            BaseUrl = body.BaseUrl,
            AuthScheme = scheme,
            DocsUrl = body.DocsUrl,
            OpenApiUrl = body.OpenApiUrl,
            HasSandbox = body.HasSandbox,
            Status = ContentStatus.Draft,
            CreatedBy = Actor(),
        };

        _dbContext.ApiCatalogEntries.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Created(
            $"/api/v1/admin/api-catalog/{entry.Id}",
            new ApiEntrySummary(entry.Id, entry.Name, entry.Slug, entry.Status.ToString(), entry.AuthScheme.ToString(), entry.BaseUrl, 0, null));
    }

    [HttpPost("{id:guid}/versions")]
    [Authorize(Policy = Permissions.Catalog.ApiWrite)]
    public async Task<ActionResult<ApiVersionRow>> AddVersion(Guid id, ApiVersionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var entry = await _dbContext.ApiCatalogEntries
            .Include(a => a.Versions)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);

        if (entry is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(body.VersionLabel))
        {
            return Refuse("VERSION_REQUIRED", "versionLabel", "A version needs a label, such as v1 or 2024-06.");
        }

        var label = body.VersionLabel.Trim();

        if (entry.Versions.Any(v => string.Equals(v.VersionLabel, label, StringComparison.OrdinalIgnoreCase)))
        {
            return Problem(
                title: "That version is already here",
                detail: $"{entry.Name} already has a version {label}.",
                statusCode: StatusCodes.Status409Conflict,
                type: PortfolioErrors.Type("VERSION_EXISTS"),
                extensions: PortfolioErrors.Extensions("VERSION_EXISTS", "versionLabel", null));
        }

        if (!Enum.TryParse<ApiVersionStatus>(body.Status, ignoreCase: true, out var status))
        {
            status = ApiVersionStatus.Stable;
        }

        if (status == ApiVersionStatus.Deprecated)
        {
            // Deprecation has its own endpoint, because it has a rule about notice that adding a
            // version does not (BR-API-03).
            return Refuse("USE_DEPRECATE", "status", "Add the version, then deprecate it, so the sunset notice is checked.");
        }

        var version = new ApiVersion
        {
            Id = Guid.NewGuid(),
            ApiCatalogEntryId = entry.Id,
            VersionLabel = label,
            Status = status,
            ReleasedOn = body.ReleasedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
            ChangelogUrl = body.ChangelogUrl,

            // The first version of anything is the one to build against; later ones are made
            // current explicitly.
            IsCurrent = entry.Versions.Count == 0,
            CreatedBy = Actor(),
        };

        _dbContext.ApiVersions.Add(version);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Created(
            $"/api/v1/admin/api-catalog/{entry.Id}",
            new ApiVersionRow(version.Id, version.VersionLabel, version.Status.ToString(), version.ReleasedOn, version.SunsetDate, version.IsCurrent));
    }

    [HttpPost("versions/{versionId:guid}/current")]
    [Authorize(Policy = Permissions.Catalog.ApiWrite)]
    public async Task<IActionResult> MakeCurrent(Guid versionId, CancellationToken cancellationToken) =>
        PortfolioErrors.Respond(this, await _portfolio.MakeCurrentAsync(versionId, Actor(), cancellationToken).ConfigureAwait(false));

    [HttpPost("versions/{versionId:guid}/deprecate")]
    [Authorize(Policy = Permissions.Catalog.ApiWrite)]
    public async Task<IActionResult> Deprecate(Guid versionId, DeprecateBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return PortfolioErrors.Respond(
            this,
            await _portfolio.DeprecateAsync(versionId, body.SunsetDate, Actor(), cancellationToken).ConfigureAwait(false));
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = Permissions.Catalog.ApiWrite)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken) =>
        PortfolioErrors.Respond(this, await _portfolio.PublishApiEntryAsync(id, Actor(), cancellationToken).ConfigureAwait(false));

    private ObjectResult Refuse(string code, string field, string message) => Problem(
        title: "That will not do",
        detail: message,
        statusCode: StatusCodes.Status422UnprocessableEntity,
        type: PortfolioErrors.Type(code),
        extensions: PortfolioErrors.Extensions(code, field, null));

    private string Actor() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

public sealed record ApiEntryBody(
    string Name,
    string? Slug,
    string Purpose,
    string? BaseUrl,
    string AuthScheme,
    string? DocsUrl,
    string? OpenApiUrl,
    bool HasSandbox,
    Guid? ProductId);

public sealed record ApiVersionBody(string VersionLabel, string? Status, DateOnly? ReleasedOn, string? ChangelogUrl);

public sealed record DeprecateBody(DateOnly SunsetDate);

public sealed record SunsetRow(string ApiName, string ApiSlug, string VersionLabel, DateOnly SunsetDate, int DaysRemaining);

public sealed record SunsetReport(int WithinDays, DateOnly AsOf, IReadOnlyList<SunsetRow> Rows, string? EmptyMessage);

public sealed record ApiEntrySummary(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    string AuthScheme,
    string? BaseUrl,
    int VersionCount,
    string? CurrentVersion);

public sealed record ApiVersionRow(
    Guid Id,
    string VersionLabel,
    string Status,
    DateOnly ReleasedOn,
    DateOnly? SunsetDate,
    bool IsCurrent);
