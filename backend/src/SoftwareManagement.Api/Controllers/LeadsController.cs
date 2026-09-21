using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Leads;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Leads;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// The lead inbox and everything done to one lead.
///
/// The ordering of the list is the only opinion this controller holds and it is the important one:
/// unanswered first, oldest unanswered before newest, because the enquiry nobody has replied to is
/// the one the company is failing right now (REQ-LEAD-009).
/// </summary>
[ApiController]
[Route("api/v1/leads")]
public sealed class LeadsController(
    AppDbContext dbContext,
    ILeadPipelineService pipeline,
    ISlaCalculator sla,
    IClock clock) : ControllerBase
{
    /// <summary>The most a caller can ask for in one page, however large a number they send.</summary>
    private const int MaxPageSize = 100;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly ILeadPipelineService _pipeline = pipeline;
    private readonly ISlaCalculator _sla = sla;
    private readonly IClock _clock = clock;

    [HttpGet]
    [Authorize(Policy = Permissions.Lead.Read)]
    public async Task<ActionResult<IReadOnlyList<LeadSummary>>> List(
        [FromQuery] string? stage,
        [FromQuery] string? view,
        [FromQuery] string? search,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var now = _clock.UtcNow;

        var query = _dbContext.Leads.AsNoTracking();

        if (Enum.TryParse<LeadStage>(stage, ignoreCase: true, out var wanted))
        {
            query = query.Where(l => l.Stage == wanted);
        }
        else
        {
            // Spam and merged records are excluded from the default view and from every count. A
            // number that includes bot submissions or the same conversation twice is not a number
            // anyone can act on (BR-LEAD-08, BR-LEAD-12).
            query = query.Where(l => l.Stage != LeadStage.Spam && l.Stage != LeadStage.Merged);
        }

        query = SavedView(query, view, now);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(l =>
                EF.Functions.Like(l.FullName, $"%{term}%")
                || (l.CompanyName != null && EF.Functions.Like(l.CompanyName, $"%{term}%"))
                || (l.Email != null && EF.Functions.Like(l.Email, $"%{term}%"))
                || (l.Message != null && EF.Functions.Like(l.Message, $"%{term}%")));
        }

        var leads = await query
            // Unanswered first, and among those the one waiting longest. Sorting by arrival alone
            // buries a week-old unanswered enquiry under this morning's answered ones.
            .OrderBy(l => l.FirstResponseAtUtc == null ? 0 : 1)
            .ThenBy(l => l.FirstResponseAtUtc == null ? l.SlaDueAtUtc : DateTime.MaxValue)
            .ThenByDescending(l => l.CreatedAtUtc)
            .Take(take)
            .Select(l => new LeadSummary(
                l.Id,
                l.FullName,
                l.Email,
                l.Phone,
                l.CompanyName,
                l.Stage.ToString(),
                l.Source.ToString(),
                l.Product == null ? null : l.Product.Name,
                l.CreatedAtUtc,
                l.SlaDueAtUtc,
                l.FirstResponseAtUtc,
                l.FirstResponseAtUtc == null && l.SlaDueAtUtc < now,
                l.FirstResponseAtUtc == null
                    ? (int)Math.Round((l.SlaDueAtUtc - now).TotalMinutes)
                    : (int?)null,
                l.Stage == LeadStage.Contacted && !l.Activities.Any(a => a.OccurredAtUtc > now.AddDays(-14))))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(leads);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Lead.Read)]
    public async Task<ActionResult<LeadDetail>> Get(Guid id, CancellationToken cancellationToken)
    {
        var lead = await _dbContext.Leads
            .AsNoTracking()
            .Include(l => l.Product)
            .Include(l => l.Organisation)
            .Include(l => l.Contact)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken).ConfigureAwait(false);

        if (lead is null)
        {
            return NotFound();
        }

        var consent = await _dbContext.FormSubmissions
            .AsNoTracking()
            .Where(s => s.LeadId == id)
            .OrderBy(s => s.CreatedAtUtc)
            .Select(s => s.Consent == null
                ? null
                : new ConsentSummary(s.Consent.ConsentText, s.Consent.ConsentVersion, s.Consent.Purpose, s.Consent.GivenAtUtc))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var timeline = await _dbContext.LeadActivities
            .AsNoTracking()
            .Where(a => a.LeadId == id)
            .OrderByDescending(a => a.OccurredAtUtc)
            .Select(a => new ActivityRow(
                a.Id,
                a.ActivityType.ToString(),
                a.Direction.ToString(),
                a.Body,
                a.OccurredAtUtc,
                a.FromStage == null ? null : a.FromStage.ToString(),
                a.ToStage == null ? null : a.ToStage.ToString(),
                a.DueAtUtc,
                a.IsCompleted,
                a.CreatedBy))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Measured, not stored: the report and this screen must not be able to disagree about how
        // long an answer took (BR-LEAD-10).
        var responseMinutes = lead.FirstResponseAtUtc is { } answered
            ? (int)Math.Round(_sla.BusinessTimeBetween(lead.CreatedAtUtc, answered).TotalMinutes)
            : (int?)null;

        return Ok(new LeadDetail(
            lead.Id,
            lead.FullName,
            lead.Email,
            lead.Phone,
            lead.CompanyName,
            lead.Message,
            lead.Stage.ToString(),
            lead.Source.ToString(),
            lead.UtmJson,
            lead.Product?.Name,
            lead.CreatedAtUtc,
            lead.SlaDueAtUtc,
            consent,
            lead.FirstResponseAtUtc,
            responseMinutes,
            lead.DisqualifyReason,
            lead.MergedIntoLeadId,
            lead.OrganisationId,
            lead.Organisation?.DisplayName,
            lead.ContactId,
            timeline));
    }

    /// <summary>
    /// Other leads that look like the same person, so nobody starts a second conversation with
    /// them (REQ-LEAD-017). Nothing is merged on the strength of this: it is a suggestion with a
    /// link, and a person decides.
    /// </summary>
    [HttpGet("{id:guid}/duplicates")]
    [Authorize(Policy = Permissions.Lead.Read)]
    public async Task<ActionResult<IReadOnlyList<DuplicateSuggestion>>> Duplicates(Guid id, CancellationToken cancellationToken)
    {
        var lead = await _dbContext.Leads.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken).ConfigureAwait(false);

        if (lead is null)
        {
            return NotFound();
        }

        var domain = EmailDomain(lead.Email);

        var candidates = await _dbContext.Leads
            .AsNoTracking()
            .Where(l => l.Id != id && l.Stage != LeadStage.Merged && l.Stage != LeadStage.Spam)
            .Where(l =>
                (lead.Email != null && l.Email == lead.Email)
                || (lead.Phone != null && l.Phone == lead.Phone)
                || (domain != null && l.Email != null && l.Email.EndsWith("@" + domain))
                || (lead.CompanyName != null && l.CompanyName == lead.CompanyName))
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(10)
            .Select(l => new { l.Id, l.FullName, l.Email, l.CompanyName, l.Stage, l.CreatedAtUtc })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // The reason is computed here rather than in SQL because it is a sentence for a person to
        // read, and it has to name the strongest match rather than the first one that happened to
        // be true.
        var suggestions = candidates
            .Select(c => new DuplicateSuggestion(
                c.Id,
                c.FullName,
                c.Email,
                c.CompanyName,
                c.Stage.ToString(),
                c.CreatedAtUtc,
                Why(lead, c.Email, c.CompanyName, domain)))
            .ToList();

        return Ok(suggestions);
    }

    [HttpPost("{id:guid}/stage")]
    [Authorize(Policy = Permissions.Lead.Write)]
    public async Task<IActionResult> ChangeStage(Guid id, StageBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!Enum.TryParse<LeadStage>(body.Stage, ignoreCase: true, out var stage))
        {
            return Problem(
                title: "That is not a stage",
                detail: $"\"{body.Stage}\" is not one of the pipeline stages.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: ErrorType("unknown-stage"),
                extensions: Extensions("UNKNOWN_STAGE", "stage"));
        }

        var result = await _pipeline.ChangeStageAsync(id, stage, body.Reason, Actor(), cancellationToken).ConfigureAwait(false);
        return Respond(result);
    }

    [HttpPost("{id:guid}/activities")]
    [Authorize(Policy = Permissions.Lead.ActivityWrite)]
    public async Task<IActionResult> AddActivity(Guid id, ActivityBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!Enum.TryParse<LeadActivityType>(body.ActivityType, ignoreCase: true, out var type)
            || !Enum.TryParse<LeadActivityDirection>(body.Direction, ignoreCase: true, out var direction))
        {
            return Problem(
                title: "That is not an activity",
                detail: "The activity type or direction is not one this system records.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: ErrorType("unknown-activity"),
                extensions: Extensions("UNKNOWN_ACTIVITY", "activityType"));
        }

        var result = await _pipeline.AddActivityAsync(
            id,
            new NewActivity(type, direction, body.Body, body.OccurredAtUtc, body.DueAtUtc),
            Actor(),
            cancellationToken).ConfigureAwait(false);

        return result.Outcome == PipelineOutcome.Done
            ? Created($"/api/v1/leads/{id}", new { id = result.SubjectId })
            : Respond(result);
    }

    /// <summary>
    /// There is no route to edit or delete an activity, and this one exists to say so out loud: a
    /// timeline that can be rewritten is not a record of anything (REQ-LEAD-011).
    /// </summary>
    [HttpDelete("{id:guid}/activities/{activityId:guid}")]
    [Authorize(Policy = Permissions.Lead.ActivityWrite)]
    public IActionResult DeleteActivity(Guid id, Guid activityId)
    {
        _ = id;
        _ = activityId;

        return Problem(
            title: "The timeline is append-only",
            detail: "An activity cannot be deleted. Add a note correcting it instead.",
            statusCode: StatusCodes.Status405MethodNotAllowed,
            type: ErrorType("append-only"),
            extensions: Extensions("APPEND_ONLY", null));
    }

    [HttpPost("{id:guid}/merge")]
    [Authorize(Policy = Permissions.Lead.Merge)]
    public async Task<IActionResult> Merge(Guid id, MergeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var result = await _pipeline.MergeAsync(id, body.OtherLeadId, Actor(), cancellationToken).ConfigureAwait(false);

        return result.Outcome == PipelineOutcome.Done
            ? Ok(new { survivorId = result.SubjectId })
            : Respond(result);
    }

    [HttpPost("{id:guid}/convert")]
    [Authorize(Policy = Permissions.Lead.Write)]
    public async Task<IActionResult> Convert(Guid id, ConvertBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var result = await _pipeline.ConvertAsync(
            id,
            new ConversionRequest(
                body.OrganisationId,
                body.OrganisationName,
                body.ContactFullName,
                body.ContactEmail,
                body.ContactPhone,
                body.ContactJobTitle),
            Actor(),
            cancellationToken).ConfigureAwait(false);

        return result.Outcome == PipelineOutcome.Done
            ? Ok(new { organisationId = result.SubjectId })
            : Respond(result);
    }

    [HttpPost("{id:guid}/spam")]
    [Authorize(Policy = Permissions.Lead.Spam)]
    public async Task<IActionResult> MarkSpam(Guid id, CancellationToken cancellationToken) =>
        Respond(await _pipeline.SetSpamAsync(id, isSpam: true, Actor(), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Restoring needs its own permission, and only the Owner has it (AZ-35): marking spam is
    /// tidying, and undoing somebody else's is a decision about their judgement.
    /// </summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = Permissions.Lead.SpamRestore)]
    public async Task<IActionResult> RestoreFromSpam(Guid id, CancellationToken cancellationToken) =>
        Respond(await _pipeline.SetSpamAsync(id, isSpam: false, Actor(), cancellationToken).ConfigureAwait(false));

    private static IQueryable<Lead> SavedView(IQueryable<Lead> query, string? view, DateTime now) => view?.ToLowerInvariant() switch
    {
        // The three questions a salesperson opens this screen to ask, named so they can be
        // bookmarked and shared rather than rebuilt from filters each morning (REQ-LEAD-009).
        "unanswered" => query.Where(l => l.FirstResponseAtUtc == null),
        "breached" => query.Where(l => l.FirstResponseAtUtc == null && l.SlaDueAtUtc < now),
        "stale" => query.Where(l => l.Stage == LeadStage.Contacted
            && !l.Activities.Any(a => a.OccurredAtUtc > now.AddDays(-14))),
        _ => query,
    };

    private static string? EmailDomain(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        return at > 0 && email is not null && at < email.Length - 1 ? email[(at + 1)..] : null;
    }

    private static string Why(Lead lead, string? otherEmail, string? otherCompany, string? domain)
    {
        if (lead.Email is not null && string.Equals(lead.Email, otherEmail, StringComparison.OrdinalIgnoreCase))
        {
            return "Same email address.";
        }

        if (lead.CompanyName is not null && string.Equals(lead.CompanyName, otherCompany, StringComparison.OrdinalIgnoreCase))
        {
            return "Same organisation name.";
        }

        return domain is not null && otherEmail is not null && otherEmail.EndsWith("@" + domain, StringComparison.OrdinalIgnoreCase)
            ? $"Another address at {domain}."
            : "Same telephone number.";
    }

    private IActionResult Respond(PipelineResult result) => result.Outcome switch
    {
        PipelineOutcome.Done => NoContent(),
        PipelineOutcome.NotFound => NotFound(),
        PipelineOutcome.Conflict => Problem(
            title: "That cannot be done twice",
            detail: result.Message,
            statusCode: StatusCodes.Status409Conflict,
            type: ErrorType(result.Code!),
            extensions: Extensions(result.Code!, result.Field)),
        _ => Problem(
            title: "That is not allowed here",
            detail: result.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: ErrorType(result.Code!),
            extensions: Extensions(result.Code!, result.Field)),
    };

    private static string ErrorType(string code) =>
        "https://softwaremanagement.example/errors/" + code.ToLowerInvariant().Replace('_', '-');

    private static Dictionary<string, object?> Extensions(string code, string? field)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code };

        if (field is not null)
        {
            extensions["field"] = field;
        }

        return extensions;
    }

    private string Actor() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

public sealed record StageBody(string Stage, string? Reason);

public sealed record ActivityBody(
    string ActivityType,
    string Direction,
    string? Body,
    DateTime? OccurredAtUtc,
    DateTime? DueAtUtc);

public sealed record MergeBody(Guid OtherLeadId);

public sealed record ConvertBody(
    Guid? OrganisationId,
    string? OrganisationName,
    string? ContactFullName,
    string? ContactEmail,
    string? ContactPhone,
    string? ContactJobTitle);

public sealed record DuplicateSuggestion(
    Guid Id,
    string FullName,
    string? Email,
    string? CompanyName,
    string Stage,
    DateTime CreatedAtUtc,
    string Reason);

public sealed record ActivityRow(
    Guid Id,
    string ActivityType,
    string Direction,
    string? Body,
    DateTime OccurredAtUtc,
    string? FromStage,
    string? ToStage,
    DateTime? DueAtUtc,
    bool IsCompleted,
    string Actor);

public sealed record LeadSummary(
    Guid Id,
    string FullName,
    string? Email,
    string? Phone,
    string? CompanyName,
    string Stage,
    string Source,
    string? Product,
    DateTime CreatedAtUtc,
    DateTime SlaDueAtUtc,
    DateTime? FirstResponseAtUtc,
    bool IsBreached,
    int? MinutesToSlaDue,
    bool IsStale);

public sealed record ConsentSummary(string Text, int Version, string Purpose, DateTime GivenAtUtc);

public sealed record LeadDetail(
    Guid Id,
    string FullName,
    string? Email,
    string? Phone,
    string? CompanyName,
    string? Message,
    string Stage,
    string Source,
    string? UtmJson,
    string? Product,
    DateTime CreatedAtUtc,
    DateTime SlaDueAtUtc,
    ConsentSummary? Consent,
    DateTime? FirstResponseAtUtc,
    int? FirstResponseBusinessMinutes,
    string? DisqualifyReason,
    Guid? MergedIntoLeadId,
    Guid? OrganisationId,
    string? OrganisationName,
    Guid? ContactId,
    IReadOnlyList<ActivityRow> Timeline);
