using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>Page sections: the ordered blocks that make a page editable without writing HTML.</summary>
[ApiController]
[Route("api/v1/admin/pages/{pageId:guid}/sections")]
public sealed class PageSectionsController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [HttpPost]
    [Authorize(Policy = Permissions.Content.PageWrite)]
    public async Task<ActionResult<SectionDetail>> Add(Guid pageId, SectionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!await _dbContext.Pages.AnyAsync(p => p.Id == pageId, cancellationToken).ConfigureAwait(false))
        {
            return NotFound();
        }

        var nextOrder = await _dbContext.PageSections
            .Where(s => s.PageId == pageId)
            .MaxAsync(s => (int?)s.SortOrder, cancellationToken).ConfigureAwait(false) ?? 0;

        var section = new PageSection
        {
            Id = Guid.NewGuid(),
            PageId = pageId,
            SectionType = Enum.TryParse<SectionType>(body.SectionType, ignoreCase: true, out var type) ? type : SectionType.RichText,
            Heading = body.Heading,
            Body = body.Body,
            MediaAssetId = body.MediaAssetId,
            SortOrder = nextOrder + 1,
            CreatedBy = ActorEmail(),
        };

        _dbContext.PageSections.Add(section);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new SectionDetail(section.Id, section.SectionType.ToString(), section.Heading, section.Body, section.SortOrder, section.IsVisible));
    }

    /// <summary>
    /// Reorders every section in one call. Positions are rewritten from the supplied order so the
    /// result is always contiguous and never has two sections claiming the same place.
    /// </summary>
    [HttpPut("order")]
    [Authorize(Policy = Permissions.Content.PageWrite)]
    public async Task<ActionResult<IReadOnlyList<SectionDetail>>> Reorder(Guid pageId, ReorderBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var sections = await _dbContext.PageSections
            .Where(s => s.PageId == pageId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (sections.Count == 0)
        {
            return NotFound();
        }

        if (body.SectionIds.Count != sections.Count || body.SectionIds.Distinct().Count() != body.SectionIds.Count)
        {
            return Problem(
                title: "Order does not match the page",
                detail: "Send every section id exactly once.",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://softwaremanagement.example/errors/section-order-mismatch");
        }

        // Move every section out of the way first: the unique (PageId, SortOrder) index would
        // otherwise reject a swap halfway through, since two sections would briefly share a slot.
        for (var i = 0; i < sections.Count; i++)
        {
            sections[i].SortOrder = -(i + 1);
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        for (var position = 0; position < body.SectionIds.Count; position++)
        {
            var section = sections.FirstOrDefault(s => s.Id == body.SectionIds[position]);
            if (section is null)
            {
                return Problem(
                    title: "Unknown section",
                    detail: $"Section {body.SectionIds[position]} is not on this page.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://softwaremanagement.example/errors/section-order-mismatch");
            }

            section.SortOrder = position + 1;
            section.ModifiedBy = ActorEmail();
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var ordered = sections
            .OrderBy(s => s.SortOrder)
            .Select(s => new SectionDetail(s.Id, s.SectionType.ToString(), s.Heading, s.Body, s.SortOrder, s.IsVisible))
            .ToList();

        return Ok(ordered);
    }

    private string ActorEmail() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

/// <summary>Header and footer menus (BR-SITE-08).</summary>
[ApiController]
[Route("api/v1/admin/navigation")]
public sealed class NavigationController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet]
    [Authorize(Policy = Permissions.Content.PageRead)]
    public async Task<ActionResult<IReadOnlyList<NavigationItemDetail>>> List(CancellationToken cancellationToken)
    {
        var items = await _dbContext.NavigationItems
            .AsNoTracking()
            .Include(n => n.Page)
            .OrderBy(n => n.Menu).ThenBy(n => n.SortOrder)
            .Select(n => new NavigationItemDetail(
                n.Id,
                n.Menu.ToString(),
                n.Label,
                n.PageId,
                n.ExternalUrl,
                n.SortOrder,
                n.Page != null && (n.Page.Status == ContentStatus.Published || n.Page.Status == ContentStatus.Modified)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(items);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Content.NavigationWrite)]
    public async Task<ActionResult<NavigationItemDetail>> Add(NavigationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var hasPage = body.PageId is not null;
        var hasUrl = !string.IsNullOrWhiteSpace(body.ExternalUrl);

        if (hasPage == hasUrl)
        {
            return Problem(
                title: "Give the item one destination",
                detail: "A menu item points at a page or at an external address, not both and not neither.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: "https://softwaremanagement.example/errors/navigation-target");
        }

        if (hasPage && !await _dbContext.Pages.AnyAsync(p => p.Id == body.PageId, cancellationToken).ConfigureAwait(false))
        {
            return Problem(title: "Unknown page", statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var menu = Enum.TryParse<NavigationMenu>(body.Menu, ignoreCase: true, out var parsed) ? parsed : NavigationMenu.Header;

        var nextOrder = await _dbContext.NavigationItems
            .Where(n => n.Menu == menu)
            .MaxAsync(n => (int?)n.SortOrder, cancellationToken).ConfigureAwait(false) ?? 0;

        var item = new NavigationItem
        {
            Id = Guid.NewGuid(),
            Menu = menu,
            Label = body.Label.Trim(),
            PageId = body.PageId,
            ExternalUrl = body.ExternalUrl,
            SortOrder = nextOrder + 1,
            OpensInNewTab = body.OpensInNewTab,
            CreatedBy = ActorEmail(),
        };

        _dbContext.NavigationItems.Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new NavigationItemDetail(item.Id, item.Menu.ToString(), item.Label, item.PageId, item.ExternalUrl, item.SortOrder, false));
    }

    private string ActorEmail() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

/// <summary>Services and the technology stack behind them (REQ-SITE-010).</summary>
[ApiController]
[Route("api/v1/admin/services")]
public sealed class ServicesController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet]
    [Authorize(Policy = Permissions.Content.PageRead)]
    public async Task<ActionResult<IReadOnlyList<ServiceDetail>>> List(CancellationToken cancellationToken)
    {
        var services = await _dbContext.Services
            .AsNoTracking()
            .Include(s => s.ServiceTechnologies).ThenInclude(st => st.Technology)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(services.Select(s => new ServiceDetail(
            s.Id,
            s.Name,
            s.Slug,
            s.Summary,
            s.IsPublished,
            [.. s.ServiceTechnologies.Select(st => st.Technology!.Name)])).ToList());
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Content.PageWrite)]
    public async Task<ActionResult<ServiceDetail>> Create(ServiceBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var slug = Slug.From(body.Name);
        if (!Slug.IsValid(slug))
        {
            return Problem(title: "Name produces no usable address", statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (await _dbContext.Services.AnyAsync(s => s.Slug == slug, cancellationToken).ConfigureAwait(false))
        {
            return Problem(title: "Service already exists", statusCode: StatusCodes.Status409Conflict);
        }

        var service = new Service
        {
            Id = Guid.NewGuid(),
            Name = body.Name.Trim(),
            Slug = slug,
            Summary = body.Summary.Trim(),
            Body = body.Body,
            IsPublished = body.IsPublished,
            CreatedBy = ActorEmail(),
        };

        foreach (var technologyId in body.TechnologyIds.Distinct())
        {
            service.ServiceTechnologies.Add(new ServiceTechnology { ServiceId = service.Id, TechnologyId = technologyId });
        }

        _dbContext.Services.Add(service);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var names = await _dbContext.Technologies
            .Where(t => body.TechnologyIds.Contains(t.Id))
            .Select(t => t.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new ServiceDetail(service.Id, service.Name, service.Slug, service.Summary, service.IsPublished, names));
    }

    [HttpPost("/api/v1/admin/technologies")]
    [Authorize(Policy = Permissions.Content.PageWrite)]
    public async Task<ActionResult<TechnologyDetail>> CreateTechnology(TechnologyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (await _dbContext.Technologies.AnyAsync(t => t.Name == body.Name, cancellationToken).ConfigureAwait(false))
        {
            return Problem(title: "Technology already listed", statusCode: StatusCodes.Status409Conflict);
        }

        var technology = new Technology
        {
            Id = Guid.NewGuid(),
            Name = body.Name.Trim(),
            Category = Enum.TryParse<TechnologyCategory>(body.Category, ignoreCase: true, out var category) ? category : TechnologyCategory.Framework,
            Proficiency = body.Proficiency,
            CreatedBy = ActorEmail(),
        };

        _dbContext.Technologies.Add(technology);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new TechnologyDetail(technology.Id, technology.Name, technology.Category.ToString(), technology.Proficiency));
    }

    [HttpDelete("/api/v1/admin/technologies/{id:guid}")]
    [Authorize(Policy = Permissions.Content.PageDelete)]
    public async Task<IActionResult> DeleteTechnology(Guid id, CancellationToken cancellationToken)
    {
        var technology = await _dbContext.Technologies.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
        if (technology is null)
        {
            return NotFound();
        }

        var usedBy = await _dbContext.ServiceTechnologies
            .Where(st => st.TechnologyId == id)
            .Select(st => st.Service!.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (usedBy.Count > 0)
        {
            return Problem(
                title: "Technology is in use",
                detail: "These services list it: " + string.Join(", ", usedBy),
                statusCode: StatusCodes.Status409Conflict,
                type: "https://softwaremanagement.example/errors/technology-in-use");
        }

        _dbContext.Technologies.Remove(technology);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    private string ActorEmail() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

/// <summary>Team members, testimonials and the site banner (REQ-SITE-012).</summary>
[ApiController]
[Route("api/v1/admin/site")]
public sealed class SiteContentController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [HttpPost("testimonials")]
    [Authorize(Policy = Permissions.Content.PageWrite)]
    public async Task<ActionResult<TestimonialDetail>> AddTestimonial(TestimonialBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        // Quoting a customer who did not agree to be quoted is a real problem, not a hypothetical
        // one, so publishing without the permission flag is refused (BR-PRJ-03).
        if (body.IsPublished && !body.HasPermission)
        {
            return Problem(
                title: "Permission not recorded",
                detail: "A testimonial can only be published once the customer's permission is recorded.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: "https://softwaremanagement.example/errors/testimonial-permission");
        }

        var testimonial = new Testimonial
        {
            Id = Guid.NewGuid(),
            AuthorName = body.AuthorName.Trim(),
            AuthorRole = body.AuthorRole.Trim(),
            OrganisationName = body.OrganisationName,
            Quote = body.Quote.Trim(),
            HasPermission = body.HasPermission,
            IsPublished = body.IsPublished,
            CreatedBy = ActorEmail(),
        };

        _dbContext.Testimonials.Add(testimonial);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new TestimonialDetail(testimonial.Id, testimonial.AuthorName, testimonial.Quote, testimonial.HasPermission, testimonial.IsPublished));
    }

    [HttpPost("team")]
    [Authorize(Policy = Permissions.Content.PageWrite)]
    public async Task<ActionResult<TeamMemberDetail>> AddTeamMember(TeamMemberBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var nextOrder = await _dbContext.TeamMembers
            .MaxAsync(t => (int?)t.SortOrder, cancellationToken).ConfigureAwait(false) ?? 0;

        var member = new TeamMember
        {
            Id = Guid.NewGuid(),
            FullName = body.FullName.Trim(),
            RoleTitle = body.RoleTitle.Trim(),
            Bio = body.Bio,
            IsPublic = body.IsPublic,
            SortOrder = nextOrder + 1,
            CreatedBy = ActorEmail(),
        };

        _dbContext.TeamMembers.Add(member);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new TeamMemberDetail(member.Id, member.FullName, member.RoleTitle, member.IsPublic));
    }

    [HttpPost("announcements")]
    [Authorize(Policy = Permissions.Content.PageWrite)]
    public async Task<ActionResult<AnnouncementDetail>> AddAnnouncement(AnnouncementBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.EndsAtUtc <= body.StartsAtUtc)
        {
            return Problem(
                title: "Dates are the wrong way round",
                detail: "The banner must end after it starts.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: "https://softwaremanagement.example/errors/announcement-window");
        }

        var announcement = new Announcement
        {
            Id = Guid.NewGuid(),
            Message = body.Message.Trim(),
            LinkUrl = body.LinkUrl,
            StartsAtUtc = body.StartsAtUtc,
            EndsAtUtc = body.EndsAtUtc,
            CreatedBy = ActorEmail(),
        };

        _dbContext.Announcements.Add(announcement);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new AnnouncementDetail(announcement.Id, announcement.Message, announcement.StartsAtUtc, announcement.EndsAtUtc));
    }

    private string ActorEmail() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

public sealed record SectionBody(string SectionType, string? Heading, string? Body, Guid? MediaAssetId);

public sealed record ReorderBody(IReadOnlyList<Guid> SectionIds);

public sealed record NavigationBody(string Menu, string Label, Guid? PageId, string? ExternalUrl, bool OpensInNewTab);

public sealed record NavigationItemDetail(Guid Id, string Menu, string Label, Guid? PageId, string? ExternalUrl, int SortOrder, bool IsVisibleToPublic);

public sealed record ServiceBody(string Name, string Summary, string? Body, bool IsPublished, IReadOnlyList<Guid> TechnologyIds);

public sealed record ServiceDetail(Guid Id, string Name, string Slug, string Summary, bool IsPublished, IReadOnlyList<string> Technologies);

public sealed record TechnologyBody(string Name, string Category, byte? Proficiency);

public sealed record TechnologyDetail(Guid Id, string Name, string Category, byte? Proficiency);

public sealed record TestimonialBody(string AuthorName, string AuthorRole, string? OrganisationName, string Quote, bool HasPermission, bool IsPublished);

public sealed record TestimonialDetail(Guid Id, string AuthorName, string Quote, bool HasPermission, bool IsPublished);

public sealed record TeamMemberBody(string FullName, string RoleTitle, string? Bio, bool IsPublic);

public sealed record TeamMemberDetail(Guid Id, string FullName, string RoleTitle, bool IsPublic);

public sealed record AnnouncementBody(string Message, string? LinkUrl, DateTime StartsAtUtc, DateTime EndsAtUtc);

public sealed record AnnouncementDetail(Guid Id, string Message, DateTime StartsAtUtc, DateTime EndsAtUtc);
