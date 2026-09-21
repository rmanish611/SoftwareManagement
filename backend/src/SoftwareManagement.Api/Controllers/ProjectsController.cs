using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Portfolio;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Domain.Portfolio;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// Delivered work: the admin side.
/// </summary>
[ApiController]
[Route("api/v1/admin/projects")]
public sealed class ProjectsController(
    AppDbContext dbContext,
    IPortfolioService portfolio) : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly IPortfolioService _portfolio = portfolio;

    [HttpGet]
    [Authorize(Policy = Permissions.Portfolio.ProjectRead)]
    public async Task<ActionResult<IReadOnlyList<ProjectSummary>>> List(
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(pageSize, 1, MaxPageSize);

        var projects = await _dbContext.Projects
            .AsNoTracking()
            .OrderByDescending(p => p.CompletedOn ?? p.StartedOn)
            .Take(take)
            .Select(p => new ProjectSummary(
                p.Id,
                p.Title,
                p.Slug,
                p.Industry,
                p.Status.ToString(),
                p.StartedOn,
                p.CompletedOn,
                p.IsFeatured,
                p.CaseStudy != null,
                p.ClientLogo != null && p.ClientLogo.HasPermission))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(projects);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Portfolio.ProjectWrite)]
    public async Task<ActionResult<ProjectSummary>> Create(ProjectBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body.Title))
        {
            return Refuse("TITLE_REQUIRED", "title", "A project needs a title.");
        }

        if (string.IsNullOrWhiteSpace(body.Industry))
        {
            return Refuse("INDUSTRY_REQUIRED", "industry", "The industry is what a visitor filters by, and the anonymised label is built from it.");
        }

        if (body.CompletedOn is { } completed && completed < body.StartedOn)
        {
            return Refuse("DATES_REVERSED", "completedOn", "A project cannot have finished before it started.");
        }

        var slug = Slug.From(string.IsNullOrWhiteSpace(body.Slug) ? body.Title : body.Slug);

        if (!Slug.IsValid(slug))
        {
            return Refuse("INVALID_SLUG", "slug", $"A slug is {Slug.MinLength} to {Slug.MaxLength} characters, lowercase letters, digits and single hyphens.");
        }

        if (await _dbContext.Projects.AnyAsync(p => p.Slug == slug, cancellationToken).ConfigureAwait(false))
        {
            return Problem(
                title: "That address is taken",
                detail: $"Another project already uses \"{slug}\".",
                statusCode: StatusCodes.Status409Conflict,
                type: PortfolioErrors.Type("SLUG_TAKEN"),
                extensions: PortfolioErrors.Extensions("SLUG_TAKEN", "slug", null));
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Title = body.Title.Trim(),
            Slug = slug,
            OrganisationId = body.OrganisationId,
            ClientDisplayName = body.ClientDisplayName,
            Industry = body.Industry.Trim(),
            Summary = body.Summary ?? string.Empty,
            StartedOn = body.StartedOn,
            CompletedOn = body.CompletedOn,
            ProductId = body.ProductId,
            ClientLogoId = body.ClientLogoId,
            IsFeatured = body.IsFeatured,
            Status = ContentStatus.Draft,
            CreatedBy = Actor(),
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Created(
            $"/api/v1/admin/projects/{project.Id}",
            new ProjectSummary(
                project.Id, project.Title, project.Slug, project.Industry, project.Status.ToString(),
                project.StartedOn, project.CompletedOn, project.IsFeatured, false, false));
    }

    [HttpPut("{id:guid}/case-study")]
    [Authorize(Policy = Permissions.Portfolio.ProjectWrite)]
    public async Task<IActionResult> SetCaseStudy(Guid id, CaseStudyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var project = await _dbContext.Projects
            .Include(p => p.CaseStudy)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

        if (project is null)
        {
            return NotFound();
        }

        var metricsJson = JsonSerializer.Serialize(body.Metrics ?? []);

        if (project.CaseStudy is { } existing)
        {
            existing.Problem = body.Problem ?? string.Empty;
            existing.Approach = body.Approach ?? string.Empty;
            existing.Outcome = body.Outcome ?? string.Empty;
            existing.MetricsJson = metricsJson;
            existing.TestimonialId = body.TestimonialId;
            existing.ModifiedBy = Actor();
        }
        else
        {
            _dbContext.CaseStudies.Add(new CaseStudy
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Problem = body.Problem ?? string.Empty,
                Approach = body.Approach ?? string.Empty,
                Outcome = body.Outcome ?? string.Empty,
                MetricsJson = metricsJson,
                TestimonialId = body.TestimonialId,
                CreatedBy = Actor(),
            });
        }

        // A project already out there whose story has changed says so, the same way a page does.
        if (project.Status == ContentStatus.Published)
        {
            project.Status = ContentStatus.Modified;
            project.ModifiedBy = Actor();
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// What a project was built with.
    ///
    /// The whole set is sent each time and replaces what was there, because the caller is a list of
    /// checkboxes: working out which ones were ticked and which unticked belongs here rather than in
    /// every screen that edits them (REQ-PRJ-008).
    /// </summary>
    [HttpPut("{id:guid}/technologies")]
    [Authorize(Policy = Permissions.Portfolio.ProjectWrite)]
    public async Task<IActionResult> SetTechnologies(Guid id, TechnologyLinkBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var project = await _dbContext.Projects
            .Include(p => p.Technologies)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

        if (project is null)
        {
            return NotFound();
        }

        var wanted = (body.TechnologyIds ?? []).Distinct().ToList();

        var known = await _dbContext.Technologies
            .Where(t => wanted.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (known.Count != wanted.Count)
        {
            return Refuse("UNKNOWN_TECHNOLOGY", "technologyIds", "One of those technologies is not in the stack list.");
        }

        foreach (var gone in project.Technologies.Where(pt => !wanted.Contains(pt.TechnologyId)).ToList())
        {
            _dbContext.ProjectTechnologies.Remove(gone);
        }

        foreach (var added in wanted.Where(w => project.Technologies.All(pt => pt.TechnologyId != w)))
        {
            _dbContext.ProjectTechnologies.Add(new ProjectTechnology { ProjectId = project.Id, TechnologyId = added });
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = Permissions.Portfolio.CaseStudyPublish)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken) =>
        PortfolioErrors.Respond(this, await _portfolio.PublishProjectAsync(id, Actor(), cancellationToken).ConfigureAwait(false));

    private ObjectResult Refuse(string code, string field, string message) => Problem(
        title: "That will not do",
        detail: message,
        statusCode: StatusCodes.Status422UnprocessableEntity,
        type: PortfolioErrors.Type(code),
        extensions: PortfolioErrors.Extensions(code, field, null));

    private string Actor() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

/// <summary>Shared by the two portfolio controllers, so one code cannot mean two things.</summary>
internal static class PortfolioErrors
{
    public static string Type(string code) =>
        "https://softwaremanagement.example/errors/" + code.ToLowerInvariant().Replace('_', '-');

    public static Dictionary<string, object?> Extensions(string code, string? field, IReadOnlyList<string>? shortfalls)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code };

        if (field is not null)
        {
            extensions["field"] = field;
        }

        // What is short, itemised: an editor should not have to read a sentence and work out which
        // box to go back to.
        if (shortfalls is { Count: > 0 })
        {
            extensions["shortfalls"] = shortfalls;
        }

        return extensions;
    }

    public static IActionResult Respond(ControllerBase controller, PortfolioResult result)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            PortfolioOutcome.Done => controller.NoContent(),
            PortfolioOutcome.NotFound => controller.NotFound(),
            PortfolioOutcome.Conflict => controller.Problem(
                title: "Not in this order",
                detail: result.Message,
                statusCode: StatusCodes.Status409Conflict,
                type: Type(result.Code!),
                extensions: Extensions(result.Code!, result.Field, result.Shortfalls)),
            _ => controller.Problem(
                title: "Not ready to publish",
                detail: result.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: Type(result.Code!),
                extensions: Extensions(result.Code!, result.Field, result.Shortfalls)),
        };
    }
}

public sealed record ProjectBody(
    string Title,
    string? Slug,
    Guid? OrganisationId,
    string? ClientDisplayName,
    string Industry,
    string? Summary,
    DateOnly StartedOn,
    DateOnly? CompletedOn,
    Guid? ProductId,
    Guid? ClientLogoId,
    bool IsFeatured);

public sealed record TechnologyLinkBody(IReadOnlyList<Guid>? TechnologyIds);

public sealed record CaseStudyBody(
    string? Problem,
    string? Approach,
    string? Outcome,
    IReadOnlyList<OutcomeMetric>? Metrics,
    Guid? TestimonialId);

public sealed record ProjectSummary(
    Guid Id,
    string Title,
    string Slug,
    string Industry,
    string Status,
    DateOnly StartedOn,
    DateOnly? CompletedOn,
    bool IsFeatured,
    bool HasCaseStudy,
    bool MayNameClient);
