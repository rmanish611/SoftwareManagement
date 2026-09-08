using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// What the public site reads. Every endpoint here is anonymous and read-only.
///
/// Two rules run through all of it. Only published content is ever returned, and a draft is a 404
/// rather than a 403, because telling an anonymous visitor that a hidden page exists is itself a
/// leak (NFR-AUTHZ-04). A `Modified` item still serves its last published version, which is the
/// whole point of that state (BR-SITE-01).
/// </summary>
[ApiController]
[Route("api/v1/public")]
[AllowAnonymous]
public sealed class PublicContentController(AppDbContext dbContext, IClock clock) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IClock _clock = clock;

    [HttpGet("pages/{slug}")]
    public async Task<ActionResult<PublicPage>> Page(string slug, CancellationToken cancellationToken)
    {
        var page = await _dbContext.Pages
            .AsNoTracking()
            .Include(p => p.Sections.Where(s => s.IsVisible).OrderBy(s => s.SortOrder))
            .Include(p => p.Seo)
            .FirstOrDefaultAsync(
                p => p.Slug == slug
                     && (p.Status == ContentStatus.Published || p.Status == ContentStatus.Modified),
                cancellationToken)
            .ConfigureAwait(false);

        if (page is null)
        {
            // Deliberately 404, not 403: the response for a draft and for a page that never
            // existed must be indistinguishable.
            var redirect = await _dbContext.Redirects
                .FirstOrDefaultAsync(r => r.FromPath == "/" + slug, cancellationToken).ConfigureAwait(false);

            if (redirect is not null)
            {
                redirect.HitCount++;
                redirect.LastHitUtc = _clock.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return RedirectPermanent(redirect.ToPath);
            }

            return NotFound();
        }

        var title = page.Title;
        var body = page.Body;

        if (page.Status == ContentStatus.Modified)
        {
            // The row holds the draft. What the public gets is the snapshot written at the last
            // publish, which is the entire reason the Modified state exists (BR-SITE-01): an editor
            // can rewrite a live page all afternoon without a visitor seeing a half-finished
            // sentence.
            var published = await _dbContext.ContentVersions
                .AsNoTracking()
                .Where(v => v.EntityType == nameof(Page) && v.EntityId == page.Id)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => v.SnapshotJson)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (published is not null)
            {
                var snapshot = System.Text.Json.JsonSerializer.Deserialize<PublishedSnapshot>(published);
                if (snapshot is not null)
                {
                    title = snapshot.Title;
                    body = snapshot.Body;
                }
            }
        }

        return Ok(new PublicPage(
            title,
            page.Slug,
            body,
            page.Seo?.MetaTitle ?? title,
            page.Seo?.MetaDescription,
            page.Seo?.CanonicalUrl,
            page.Seo?.NoIndex ?? false,
            [.. page.Sections.Select(s => new PublicSection(s.SectionType.ToString(), s.Heading, s.Body))]));
    }

    [HttpGet("navigation")]
    public async Task<ActionResult<IReadOnlyList<PublicNavigationItem>>> Navigation(CancellationToken cancellationToken)
    {
        // A menu item pointing at an unpublished page is hidden rather than rendered as a dead
        // link (EX-114).
        var items = await _dbContext.NavigationItems
            .AsNoTracking()
            .Include(n => n.Page)
            .Where(n => n.ExternalUrl != null
                        || (n.Page != null && (n.Page.Status == ContentStatus.Published || n.Page.Status == ContentStatus.Modified)))
            .OrderBy(n => n.Menu).ThenBy(n => n.SortOrder)
            .Select(n => new PublicNavigationItem(
                n.Menu.ToString(),
                n.Label,
                n.ExternalUrl ?? "/" + n.Page!.Slug,
                n.OpensInNewTab))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(items);
    }

    [HttpGet("services")]
    public async Task<ActionResult<IReadOnlyList<PublicService>>> Services(CancellationToken cancellationToken)
    {
        var services = await _dbContext.Services
            .AsNoTracking()
            .Include(s => s.ServiceTechnologies).ThenInclude(st => st.Technology)
            .Where(s => s.IsPublished)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(services.Select(s => new PublicService(
            s.Name,
            s.Slug,
            s.Summary,
            [.. s.ServiceTechnologies.Select(st => st.Technology!.Name).Order()])).ToList());
    }

    [HttpGet("company")]
    public async Task<ActionResult<PublicCompany>> Company(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.SystemSettings
            .AsNoTracking()
            .Where(s => !s.IsSecret && s.Key.StartsWith("company."))
            .ToDictionaryAsync(s => s.Key, s => s.Value ?? string.Empty, cancellationToken).ConfigureAwait(false);

        var team = await _dbContext.TeamMembers
            .AsNoTracking()
            .Where(t => t.IsPublic)
            .OrderBy(t => t.SortOrder)
            .Select(t => new PublicTeamMember(t.FullName, t.RoleTitle, t.Bio))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Only testimonials whose permission was recorded (BR-PRJ-03).
        var testimonials = await _dbContext.Testimonials
            .AsNoTracking()
            .Where(t => t.IsPublished && t.HasPermission)
            .OrderBy(t => t.SortOrder)
            .Select(t => new PublicTestimonial(t.Quote, t.AuthorName, t.AuthorRole, t.OrganisationName))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var now = _clock.UtcNow;
        var announcement = await _dbContext.Announcements
            .AsNoTracking()
            .Where(a => a.StartsAtUtc <= now && a.EndsAtUtc > now)
            .OrderByDescending(a => a.StartsAtUtc)
            .Select(a => new PublicAnnouncement(a.Message, a.LinkUrl, a.Severity.ToString()))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new PublicCompany(
            settings.GetValueOrDefault("company.name", "Software Management"),
            settings.GetValueOrDefault("company.city", string.Empty),
            team,
            testimonials,
            announcement));
    }

    [HttpGet("technologies")]
    public async Task<ActionResult<IReadOnlyList<PublicTechnology>>> Technologies(CancellationToken cancellationToken)
    {
        var technologies = await _dbContext.Technologies
            .AsNoTracking()
            .OrderBy(t => t.Category).ThenBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new PublicTechnology(t.Name, t.Category.ToString(), t.Proficiency))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(technologies);
    }
}

public sealed record PublicPage(
    string Title,
    string Slug,
    string? Body,
    string MetaTitle,
    string? MetaDescription,
    string? CanonicalUrl,
    bool NoIndex,
    IReadOnlyList<PublicSection> Sections);

public sealed record PublicSection(string SectionType, string? Heading, string? Body);

/// <summary>The shape the publishing service writes into a content version snapshot.</summary>
internal sealed record PublishedSnapshot(string Title, string? Body, List<Guid> MediaAssetIds);

public sealed record PublicNavigationItem(string Menu, string Label, string Href, bool OpensInNewTab);

public sealed record PublicService(string Name, string Slug, string Summary, IReadOnlyList<string> Technologies);

public sealed record PublicTechnology(string Name, string Category, byte? Proficiency);

public sealed record PublicTeamMember(string FullName, string RoleTitle, string? Bio);

public sealed record PublicTestimonial(string Quote, string AuthorName, string AuthorRole, string? OrganisationName);

public sealed record PublicAnnouncement(string Message, string? LinkUrl, string Severity);

public sealed record PublicCompany(
    string Name,
    string City,
    IReadOnlyList<PublicTeamMember> Team,
    IReadOnlyList<PublicTestimonial> Testimonials,
    PublicAnnouncement? Announcement);
