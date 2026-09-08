using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// Page authoring for editors. Publishing, scheduling and restore go through the publishing
/// service so the rules are identical for every content type.
/// </summary>
[ApiController]
[Route("api/v1/admin/pages")]
public sealed class PagesController(
    AppDbContext dbContext,
    IPublishingService publishing,
    ISlugService slugs,
    IEditorTimeZone timeZone) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IPublishingService _publishing = publishing;
    private readonly ISlugService _slugs = slugs;
    private readonly IEditorTimeZone _timeZone = timeZone;

    [HttpGet]
    [Authorize(Policy = Permissions.Content.PageRead)]
    public async Task<ActionResult<IReadOnlyList<PageSummary>>> List(
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // NFR-PERF-04: a caller may ask for more, and gets the ceiling instead of an error.
        var take = Math.Clamp(pageSize, 1, 100);

        var pages = await _dbContext.Pages
            .AsNoTracking()
            .OrderBy(p => p.Title)
            .Take(take)
            .Select(p => new PageSummary(p.Id, p.Title, p.Slug, p.Status.ToString(), p.PublishedAtUtc, p.PublishAtUtc))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(pages);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Content.PageRead)]
    public async Task<ActionResult<PageDetail>> Get(Guid id, CancellationToken cancellationToken)
    {
        var page = await _dbContext.Pages
            .AsNoTracking()
            .Include(p => p.Sections.OrderBy(s => s.SortOrder))
            .Include(p => p.Seo)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

        return page is null ? NotFound() : Ok(PageDetail.From(page));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Content.PageWrite)]
    public async Task<ActionResult<PageDetail>> Create(CreatePageBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        // A slug the caller typed is validated as typed. Silently truncating it would change the
        // address they asked for without telling them. A slug derived from the title may be
        // shortened, because nobody chose its exact length.
        var explicitSlug = !string.IsNullOrWhiteSpace(body.Slug);

        if (explicitSlug && body.Slug!.Length > Slug.MaxLength)
        {
            return Problem(
                title: "Slug is too long",
                detail: $"A slug is at most {Slug.MaxLength} characters.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: "https://softwaremanagement.example/errors/invalid-slug");
        }

        var slug = Slug.From(explicitSlug ? body.Slug : body.Title);

        if (!Slug.IsValid(slug))
        {
            return Problem(
                title: "Slug is not valid",
                detail: $"A slug is {Slug.MinLength} to {Slug.MaxLength} characters, lowercase letters, digits and single hyphens.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: "https://softwaremanagement.example/errors/invalid-slug");
        }

        if (!await _slugs.IsAvailableAsync(nameof(Page), slug, null, cancellationToken).ConfigureAwait(false))
        {
            var suggestion = await _slugs.SuggestAsync(nameof(Page), slug, cancellationToken).ConfigureAwait(false);
            return Problem(
                title: "Slug is taken",
                detail: $"That address is already in use. Try \"{suggestion}\".",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://softwaremanagement.example/errors/slug-taken");
        }

        var page = new Page
        {
            Id = Guid.NewGuid(),
            Title = body.Title.Trim(),
            Slug = slug,
            PageType = Enum.TryParse<PageType>(body.PageType, ignoreCase: true, out var type) ? type : PageType.Custom,
            Body = body.Body,
            Status = ContentStatus.Draft,
            CreatedBy = ActorEmail(),
            Seo = new SeoMetadata
            {
                Id = Guid.NewGuid(),
                MetaTitle = body.MetaTitle?.Trim() ?? body.Title.Trim(),
                MetaDescription = body.MetaDescription,
                CreatedBy = ActorEmail(),
            },
        };

        _dbContext.Pages.Add(page);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return CreatedAtAction(nameof(Get), new { id = page.Id }, PageDetail.From(page));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Content.PageWrite)]
    public async Task<ActionResult<PageDetail>> Update(Guid id, UpdatePageBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var page = await _dbContext.Pages.Include(p => p.Seo).FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        if (page is null)
        {
            return NotFound();
        }

        var previousSlug = page.Slug;
        var wasPublished = page.Status is ContentStatus.Published or ContentStatus.Modified;

        if (!string.IsNullOrWhiteSpace(body.Slug) && body.Slug != page.Slug)
        {
            var slug = Slug.From(body.Slug);
            if (!Slug.IsValid(slug))
            {
                return Problem(title: "Slug is not valid", statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            if (!await _slugs.IsAvailableAsync(nameof(Page), slug, id, cancellationToken).ConfigureAwait(false))
            {
                return Problem(title: "Slug is taken", statusCode: StatusCodes.Status409Conflict);
            }

            page.Slug = slug;
        }

        page.Title = body.Title.Trim();
        page.Body = body.Body;
        page.ModifiedBy = ActorEmail();

        if (page.Seo is not null && body.MetaTitle is not null)
        {
            page.Seo.MetaTitle = body.MetaTitle.Trim();
            page.Seo.MetaDescription = body.MetaDescription;
            page.Seo.ModifiedBy = ActorEmail();
        }

        // Editing a live page does not change what the public sees until it is published again
        // (BR-SITE-01). This one line is the whole point of the Modified state.
        if (page.Status == ContentStatus.Published)
        {
            page.Status = ContentStatus.Modified;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // A renamed page that was live leaves its old address working (BR-SITE-06).
        if (wasPublished && previousSlug != page.Slug)
        {
            await _slugs.RecordRenameAsync("/" + previousSlug, "/" + page.Slug, cancellationToken).ConfigureAwait(false);
        }

        return Ok(PageDetail.From(page));
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = Permissions.Content.PagePublish)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken) =>
        Respond(await _publishing.PublishPageAsync(id, ActorEmail(), cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Policy = Permissions.Content.PagePublish)]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken cancellationToken) =>
        Respond(await _publishing.UnpublishPageAsync(id, ActorEmail(), cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/schedule")]
    [Authorize(Policy = Permissions.Content.PagePublish)]
    public async Task<ActionResult<ScheduleResponse>> Schedule(Guid id, ScheduleBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        // The editor typed a wall-clock time in their own timezone (BR-SITE-04, S-06).
        var publishAtUtc = _timeZone.ToUtc(body.PublishAtLocal);
        var unpublishAtUtc = body.UnpublishAtLocal is null ? (DateTime?)null : _timeZone.ToUtc(body.UnpublishAtLocal.Value);

        var result = await _publishing
            .SchedulePageAsync(id, publishAtUtc, unpublishAtUtc, ActorEmail(), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            // Respond() produces the shared problem document for every publishing outcome, so the
            // schedule endpoint reports a blocked reference exactly the way publish does.
            return (ActionResult)Respond(result);
        }

        // Showing the server-time equivalent is what stops an editor in another timezone being
        // surprised by when the page actually appears.
        return Ok(new ScheduleResponse(
            publishAtUtc,
            unpublishAtUtc,
            _timeZone.TimeZoneId,
            _timeZone.ToServerLocal(publishAtUtc)));
    }

    [HttpPost("bulk-publish")]
    [Authorize(Policy = Permissions.Content.PagePublish)]
    public async Task<ActionResult<IReadOnlyList<BulkPublishOutcome>>> BulkPublish(BulkPublishBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.PageIds.Count == 0)
        {
            return Problem(
                title: "Nothing selected",
                detail: "Select at least one page to publish.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://softwaremanagement.example/errors/empty-selection");
        }

        var results = await _publishing.PublishManyAsync(body.PageIds, ActorEmail(), cancellationToken).ConfigureAwait(false);
        return Ok(results);
    }

    [HttpGet("{id:guid}/versions")]
    [Authorize(Policy = Permissions.Content.PageRead)]
    public async Task<ActionResult<IReadOnlyList<VersionSummary>>> Versions(Guid id, CancellationToken cancellationToken)
    {
        var versions = await _dbContext.ContentVersions
            .AsNoTracking()
            .Where(v => v.EntityType == nameof(Page) && v.EntityId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new VersionSummary(v.VersionNumber, v.CreatedAtUtc, v.CreatedBy))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(versions);
    }

    [HttpPost("{id:guid}/versions/{versionNumber:int}/restore")]
    [Authorize(Policy = Permissions.Content.VersionRestore)]
    public async Task<IActionResult> Restore(Guid id, int versionNumber, CancellationToken cancellationToken) =>
        Respond(await _publishing.RestoreVersionAsync(id, versionNumber, ActorEmail(), cancellationToken).ConfigureAwait(false));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Content.PageDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var page = await _dbContext.Pages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        if (page is null)
        {
            return NotFound();
        }

        if (page.IsSystemPage)
        {
            return Problem(
                title: "System page",
                detail: "This page is part of the site's structure and cannot be deleted.",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://softwaremanagement.example/errors/system-page");
        }

        var referrers = await _dbContext.NavigationItems
            .Where(n => n.PageId == id)
            .Select(n => n.Label)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (referrers.Count > 0)
        {
            return Problem(
                title: "Page is in use",
                detail: "Remove it from the menu first: " + string.Join(", ", referrers),
                statusCode: StatusCodes.Status409Conflict,
                type: "https://softwaremanagement.example/errors/page-in-use");
        }

        _dbContext.Pages.Remove(page);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    private IActionResult Respond(PublishResult result) => result.Outcome switch
    {
        PublishOutcome.Published or PublishOutcome.Unpublished or PublishOutcome.Scheduled or PublishOutcome.Restored
            => NoContent(),

        PublishOutcome.NotFound or PublishOutcome.VersionNotFound => NotFound(),

        PublishOutcome.BlockedByUnpublishedReferences => Problem(
            title: "Cannot publish yet",
            detail: "This page points at content the public cannot see: " + string.Join("; ", result.BlockingReferences),
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: "https://softwaremanagement.example/errors/unpublished-reference"),

        PublishOutcome.ScheduleTooSoon => Problem(
            title: "Scheduled time is too soon",
            detail: $"Schedule at least {ScheduleRules.MinimumLeadTime.TotalMinutes:0} minutes ahead.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: "https://softwaremanagement.example/errors/schedule-too-soon"),

        PublishOutcome.ScheduleTooFar => Problem(
            title: "Scheduled time is too far ahead",
            detail: $"Schedule at most {ScheduleRules.MaximumLeadTime.TotalDays:0} days ahead.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: "https://softwaremanagement.example/errors/schedule-too-far"),

        PublishOutcome.UnpublishBeforePublish => Problem(
            title: "Dates are the wrong way round",
            detail: "The unpublish time must come after the publish time.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: "https://softwaremanagement.example/errors/schedule-order"),

        PublishOutcome.VersionReferencesMissingContent => Problem(
            title: "Cannot restore that version",
            detail: "It refers to media that no longer exists, so restoring it would produce a broken page.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: "https://softwaremanagement.example/errors/version-missing-content"),

        _ => Problem(statusCode: StatusCodes.Status500InternalServerError),
    };

    private string ActorEmail() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

public sealed record CreatePageBody(string Title, string? Slug, string? PageType, string? Body, string? MetaTitle, string? MetaDescription);

public sealed record UpdatePageBody(string Title, string? Slug, string? Body, string? MetaTitle, string? MetaDescription);

public sealed record ScheduleBody(DateTime PublishAtLocal, DateTime? UnpublishAtLocal);

public sealed record ScheduleResponse(DateTime PublishAtUtc, DateTime? UnpublishAtUtc, string EditorTimeZone, DateTime PublishAtServerLocal);

public sealed record BulkPublishBody(IReadOnlyList<Guid> PageIds);

public sealed record PageSummary(Guid Id, string Title, string Slug, string Status, DateTime? PublishedAtUtc, DateTime? PublishAtUtc);

public sealed record VersionSummary(int VersionNumber, DateTime CreatedAtUtc, string CreatedBy);

public sealed record PageDetail(
    Guid Id,
    string Title,
    string Slug,
    string PageType,
    string Status,
    string? Body,
    string? MetaTitle,
    string? MetaDescription,
    DateTime? PublishedAtUtc,
    DateTime? PublishAtUtc,
    IReadOnlyList<SectionDetail> Sections)
{
    public static PageDetail From(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new PageDetail(
            page.Id,
            page.Title,
            page.Slug,
            page.PageType.ToString(),
            page.Status.ToString(),
            page.Body,
            page.Seo?.MetaTitle,
            page.Seo?.MetaDescription,
            page.PublishedAtUtc,
            page.PublishAtUtc,
            [.. page.Sections.OrderBy(s => s.SortOrder).Select(s => new SectionDetail(s.Id, s.SectionType.ToString(), s.Heading, s.Body, s.SortOrder, s.IsVisible))]);
    }
}

public sealed record SectionDetail(Guid Id, string SectionType, string? Heading, string? Body, int SortOrder, bool IsVisible);
