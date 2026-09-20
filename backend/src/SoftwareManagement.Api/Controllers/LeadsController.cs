using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Leads;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// Reading the enquiries that have arrived.
///
/// This phase captures leads; working them, assigning them and merging them arrives with the inbox
/// in P08. What is here is what proves capture worked: the list, one lead, and the consent record
/// behind it, which is the evidence a privacy request would be answered from.
/// </summary>
[ApiController]
[Route("api/v1/leads")]
public sealed class LeadsController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet]
    [Authorize(Policy = Permissions.Lead.Read)]
    public async Task<ActionResult<IReadOnlyList<LeadSummary>>> List(
        [FromQuery] string? stage,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.Leads.AsNoTracking();

        if (Enum.TryParse<LeadStage>(stage, ignoreCase: true, out var wanted))
        {
            query = query.Where(l => l.Stage == wanted);
        }
        else
        {
            // Spam is excluded from the default view and from every count. A number that includes
            // bot submissions is not a number anyone can act on (REQ-LEAD-009).
            query = query.Where(l => l.Stage != LeadStage.Spam);
        }

        var leads = await query
            .OrderByDescending(l => l.CreatedAtUtc)
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
                l.FirstResponseAtUtc))
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
            consent));
    }
}

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
    DateTime? FirstResponseAtUtc);

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
    ConsentSummary? Consent);
