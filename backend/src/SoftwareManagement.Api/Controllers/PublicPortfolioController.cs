using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Domain.Portfolio;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// The portfolio and the developer directory, as a visitor sees them.
///
/// Nothing here names a client the client has not agreed to be named: the anonymised label is
/// computed on the server rather than left to the page, so a new client of this API cannot get it
/// wrong (BR-PRJ-01).
/// </summary>
[ApiController]
[Route("api/v1/public")]
[AllowAnonymous]
public sealed class PublicPortfolioController(AppDbContext dbContext) : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet("projects")]
    public async Task<ActionResult<IReadOnlyList<PublicProjectCard>>> Projects(
        [FromQuery] string? industry,
        [FromQuery] int pageSize = 24,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _dbContext.Projects
            .AsNoTracking()
            .Include(p => p.ClientLogo)
            .Where(p => p.Status == ContentStatus.Published || p.Status == ContentStatus.Modified);

        if (!string.IsNullOrWhiteSpace(industry))
        {
            var wanted = industry.Trim();
            query = query.Where(p => p.Industry == wanted);
        }

        var projects = await query
            .OrderByDescending(p => p.IsFeatured)
            .ThenByDescending(p => p.CompletedOn ?? p.StartedOn)
            .Take(take)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // The label is computed after the query rather than inside it, because the rule is a
        // sentence about permission and not something to express twice in SQL.
        return Ok(projects.ConvertAll(p => new PublicProjectCard(
            p.Title,
            p.Slug,
            p.Industry,
            p.Summary,
            p.PublicClientLabel(),
            p.StartedOn,
            p.CompletedOn,
            p.IsFeatured)));
    }

    [HttpGet("projects/industries")]
    public async Task<ActionResult<IReadOnlyList<string>>> Industries(CancellationToken cancellationToken)
    {
        var industries = await _dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published || p.Status == ContentStatus.Modified)
            .Select(p => p.Industry)
            .Distinct()
            .OrderBy(i => i)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(industries);
    }

    [HttpGet("projects/{slug}")]
    public async Task<ActionResult<PublicCaseStudy>> Project(string slug, CancellationToken cancellationToken)
    {
        var project = await _dbContext.Projects
            .AsNoTracking()
            .Include(p => p.ClientLogo)
            .Include(p => p.CaseStudy!).ThenInclude(c => c.Testimonial)
            .Include(p => p.Product)
            .FirstOrDefaultAsync(
                p => p.Slug == slug && (p.Status == ContentStatus.Published || p.Status == ContentStatus.Modified),
                cancellationToken).ConfigureAwait(false);

        // A draft's address is a 404 to a stranger, never a 403: a 403 would confirm it exists
        // (NFR-AUTHZ-04).
        if (project is null)
        {
            return NotFound();
        }

        var study = project.CaseStudy;

        return Ok(new PublicCaseStudy(
            project.Title,
            project.Slug,
            project.Industry,
            project.Summary,
            project.PublicClientLabel(),
            project.StartedOn,
            project.CompletedOn,
            project.Product?.Name,
            study?.Problem,
            study?.Approach,
            study?.Outcome,
            study is null ? [] : [.. study.Metrics()],

            // A testimonial needs the author's name and role and an explicit permission flag; an
            // anonymous one is not published, and neither is one whose author has not agreed
            // (BR-PRJ-03).
            study?.Testimonial is { HasPermission: true, IsPublished: true } quote
                && !string.IsNullOrWhiteSpace(quote.AuthorName)
                && !string.IsNullOrWhiteSpace(quote.AuthorRole)
                    ? new PublicTestimonial(quote.Quote, quote.AuthorName, quote.AuthorRole, quote.OrganisationName)
                    : null));
    }

    /// <summary>
    /// The logo wall: only the clients who said we may.
    ///
    /// Permission is a column on the row, so withdrawing it removes the logo on the next request
    /// rather than on the next deployment (REQ-PRJ-007).
    /// </summary>
    [HttpGet("client-logos")]
    public async Task<ActionResult<IReadOnlyList<PublicClientLogo>>> ClientLogos(CancellationToken cancellationToken)
    {
        var logos = await _dbContext.ClientLogos
            .AsNoTracking()
            .Include(l => l.MediaAsset)
            .Where(l => l.HasPermission && l.MediaAsset != null)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.DisplayName)
            .Select(l => new PublicClientLogo(
                l.DisplayName,
                "/api/v1/public/media/" + l.MediaAsset!.StorageKey,
                l.MediaAsset.AltText ?? l.DisplayName))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(logos);
    }

    /// <summary>
    /// The stack page's evidence: each technology and the delivered work that used it.
    ///
    /// A technology nobody has shipped with yet is still listed, with a count of zero. Dropping it
    /// would make the page quietly disagree with the stack page next to it (REQ-PRJ-008).
    /// </summary>
    [HttpGet("technologies/usage")]
    public async Task<ActionResult<IReadOnlyList<TechnologyUsage>>> TechnologyUsage(CancellationToken cancellationToken)
    {
        var technologies = await _dbContext.Technologies
            .AsNoTracking()
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new { t.Id, t.Name, Category = t.Category.ToString() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var used = await _dbContext.ProjectTechnologies
            .AsNoTracking()
            .Where(pt => pt.Project!.Status == ContentStatus.Published || pt.Project!.Status == ContentStatus.Modified)
            .Select(pt => new { pt.TechnologyId, pt.Project!.Title, pt.Project.Slug })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var byTechnology = used
            .GroupBy(u => u.TechnologyId)
            .ToDictionary(g => g.Key, g => g.Select(u => new UsedOnProject(u.Title, u.Slug)).OrderBy(u => u.Title).ToList());

        return Ok(technologies.ConvertAll(t =>
        {
            var projects = byTechnology.TryGetValue(t.Id, out var rows) ? rows : [];
            return new TechnologyUsage(t.Name, t.Category, projects.Count, projects);
        }));
    }

    /// <summary>
    /// The public URL set, as a sitemap.
    ///
    /// P13 adds robots, the index split and last-modified dates from the SEO rows; what is here is
    /// the part REQ-API-007 asks for - every published API entry and project present, and nothing
    /// that is still a draft.
    /// </summary>
    [HttpGet("sitemap.xml")]
    [Produces("application/xml")]
    public async Task<IActionResult> Sitemap(CancellationToken cancellationToken)
    {
        var projects = await _dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published || p.Status == ContentStatus.Modified)
            .Select(p => p.Slug)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var apis = await _dbContext.ApiCatalogEntries
            .AsNoTracking()
            .Where(a => a.Status == ContentStatus.Published || a.Status == ContentStatus.Modified)
            .Select(a => a.Slug)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        builder.AppendLine("  <url><loc>/projects</loc></url>");
        builder.AppendLine("  <url><loc>/developers</loc></url>");

        foreach (var slug in projects.Order(StringComparer.Ordinal))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"  <url><loc>/projects/{WebUtility.HtmlEncode(slug)}</loc></url>");
        }

        foreach (var slug in apis.Order(StringComparer.Ordinal))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"  <url><loc>/developers/{WebUtility.HtmlEncode(slug)}</loc></url>");
        }

        builder.AppendLine("</urlset>");
        return Content(builder.ToString(), "application/xml");
    }

    [HttpGet("apis")]
    public async Task<ActionResult<IReadOnlyList<PublicApiEntry>>> Apis(CancellationToken cancellationToken)
    {
        var entries = await _dbContext.ApiCatalogEntries
            .AsNoTracking()
            .Include(a => a.Versions)
            .Include(a => a.Product)
            .Where(a => a.Status == ContentStatus.Published || a.Status == ContentStatus.Modified)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(entries.ConvertAll(entry =>
        {
            var current = entry.Versions.FirstOrDefault(v => v.IsCurrent);

            return new PublicApiEntry(
                entry.Name,
                entry.Slug,
                entry.Purpose,
                entry.BaseUrl,
                entry.AuthScheme.ToString(),
                entry.DocsUrl,
                entry.OpenApiUrl,
                entry.HasSandbox,
                entry.Product?.Name,
                current?.VersionLabel,

                // Every version, including the ones going away and when: a developer already on an
                // old one needs the date more than a developer choosing a new one does (REQ-API-003).
                [.. entry.Versions
                    .OrderByDescending(v => v.IsCurrent).ThenByDescending(v => v.ReleasedOn)
                    .Select(v => new PublicApiVersion(
                        v.VersionLabel, v.Status.ToString(), v.ReleasedOn, v.SunsetDate, v.IsCurrent, v.ChangelogUrl))]);
        }));
    }
}

public sealed record PublicClientLogo(string Name, string ImageUrl, string AltText);

public sealed record UsedOnProject(string Title, string Slug);

public sealed record TechnologyUsage(string Name, string Category, int ProjectCount, IReadOnlyList<UsedOnProject> Projects);

public sealed record PublicProjectCard(
    string Title,
    string Slug,
    string Industry,
    string Summary,
    string Client,
    DateOnly StartedOn,
    DateOnly? CompletedOn,
    bool IsFeatured);

public sealed record PublicCaseStudy(
    string Title,
    string Slug,
    string Industry,
    string Summary,
    string Client,
    DateOnly StartedOn,
    DateOnly? CompletedOn,
    string? Product,
    string? Problem,
    string? Approach,
    string? Outcome,
    IReadOnlyList<OutcomeMetric> Metrics,
    PublicTestimonial? Testimonial);

public sealed record PublicApiVersion(
    string VersionLabel,
    string Status,
    DateOnly ReleasedOn,
    DateOnly? SunsetDate,
    bool IsCurrent,
    string? ChangelogUrl);

public sealed record PublicApiEntry(
    string Name,
    string Slug,
    string Purpose,
    string? BaseUrl,
    string AuthScheme,
    string? DocsUrl,
    string? OpenApiUrl,
    bool HasSandbox,
    string? Product,
    string? CurrentVersion,
    IReadOnlyList<PublicApiVersion> Versions);
