using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Sales;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Identity;
using SoftwareManagement.Domain.Sales;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// Quotes.
///
/// One refusal here is a 403 rather than a 422 and it is worth saying why: a discount above the
/// threshold is not a malformed request, it is a request this person is not allowed to make. The
/// same body from the Owner succeeds (BR-SALE-04).
/// </summary>
[ApiController]
[Route("api/v1/quotes")]
public sealed class QuotesController(AppDbContext dbContext, IQuoteService quotes) : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly IQuoteService _quotes = quotes;

    [HttpGet]
    [Authorize(Policy = Permissions.Sales.QuoteRead)]
    public async Task<ActionResult<IReadOnlyList<QuoteSummary>>> List(
        [FromQuery] string? status,
        [FromQuery] Guid? organisationId,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = _dbContext.Quotes.AsNoTracking();

        if (Enum.TryParse<QuoteStatus>(status, ignoreCase: true, out var wanted))
        {
            query = query.Where(q => q.Status == wanted);
        }

        if (organisationId is { } id)
        {
            query = query.Where(q => q.OrganisationId == id);
        }

        var rows = await query
            .OrderByDescending(q => q.CreatedAtUtc)
            .Take(take)
            .Select(q => new QuoteSummary(
                q.Id,
                q.QuoteNumber,
                q.Organisation!.DisplayName,
                q.Status.ToString(),
                q.Currency,
                q.GrandTotal,
                q.IssuedOn,
                q.ValidUntil,
                q.Lines.Count))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Sales.QuoteRead)]
    public async Task<ActionResult<QuoteDetail>> Get(Guid id, CancellationToken cancellationToken)
    {
        var quote = await _dbContext.Quotes
            .AsNoTracking()
            .Include(q => q.Organisation)
            .Include(q => q.Contact)
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken).ConfigureAwait(false);

        if (quote is null)
        {
            return NotFound();
        }

        return Ok(new QuoteDetail(
            quote.Id,
            quote.QuoteNumber,
            quote.OrganisationId,
            quote.Organisation!.DisplayName,
            quote.ContactId,
            quote.Contact!.FullName,
            quote.Contact.Email,
            quote.Status.ToString(),
            quote.Currency,
            quote.SubTotal,
            quote.DiscountTotal,
            quote.TaxTotal,
            quote.GrandTotal,
            quote.IssuedOn,
            quote.ValidUntil,
            quote.Notes,
            quote.RejectReason,
            quote.RevisionOfQuoteId,
            quote.Status == QuoteStatus.Draft,
            [.. quote.Lines.OrderBy(l => l.SortOrder).Select(l => new QuoteLineRow(
                l.Id,
                l.ProductId,
                l.PricingPlanId,
                l.Description,
                l.Quantity,
                l.UnitPrice,
                l.DiscountAmount,
                l.TaxRatePercent,
                l.LineTotal,
                l.TaxAmount,
                l.SortOrder))]));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Sales.QuoteWrite)]
    public async Task<IActionResult> Create(CreateQuoteBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var result = await _quotes
            .CreateAsync(new NewQuote(body.OrganisationId, body.ContactId, body.LeadId, body.Notes), Actor(), cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome == QuoteOutcome.Done
            ? Created($"/api/v1/quotes/{result.QuoteId}", new { id = result.QuoteId, quoteNumber = result.QuoteNumber })
            : Respond(result);
    }

    [HttpPost("{id:guid}/lines")]
    [Authorize(Policy = Permissions.Sales.QuoteWrite)]
    public async Task<IActionResult> AddLine(Guid id, QuoteLineBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body.Description))
        {
            return Problem(
                title: "That will not do",
                detail: "A line needs a description; the customer reads it, not the product id.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: ErrorType("DESCRIPTION_REQUIRED"),
                extensions: Extensions("DESCRIPTION_REQUIRED", "description"));
        }

        var result = await _quotes.AddLineAsync(
            id,
            new NewQuoteLine(
                body.ProductId,
                body.PricingPlanId,
                body.Description,
                body.Quantity,
                body.UnitPrice,
                body.DiscountAmount,
                body.TaxRatePercent ?? 18.00m),
            SalesUser(),
            cancellationToken).ConfigureAwait(false);

        return Respond(result);
    }

    [HttpDelete("{id:guid}/lines/{lineId:guid}")]
    [Authorize(Policy = Permissions.Sales.QuoteWrite)]
    public async Task<IActionResult> RemoveLine(Guid id, Guid lineId, CancellationToken cancellationToken) =>
        Respond(await _quotes.RemoveLineAsync(id, lineId, Actor(), cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/send")]
    [Authorize(Policy = Permissions.Sales.QuoteSend)]
    public async Task<IActionResult> Send(Guid id, SendQuoteBody? body, CancellationToken cancellationToken) =>
        Respond(await _quotes.SendAsync(id, body?.ValidUntil, Actor(), cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/revise")]
    [Authorize(Policy = Permissions.Sales.QuoteWrite)]
    public async Task<IActionResult> Revise(Guid id, CancellationToken cancellationToken)
    {
        var result = await _quotes.ReviseAsync(id, Actor(), cancellationToken).ConfigureAwait(false);

        return result.Outcome == QuoteOutcome.Done
            ? Created($"/api/v1/quotes/{result.QuoteId}", new { id = result.QuoteId, quoteNumber = result.QuoteNumber })
            : Respond(result);
    }

    [HttpPost("{id:guid}/accept")]
    [Authorize(Policy = Permissions.Sales.QuoteAccept)]
    public async Task<IActionResult> Accept(Guid id, CancellationToken cancellationToken) =>
        Respond(await _quotes.AcceptAsync(id, Actor(), cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = Permissions.Sales.QuoteAccept)]
    public async Task<IActionResult> Reject(Guid id, RejectQuoteBody? body, CancellationToken cancellationToken) =>
        Respond(await _quotes.RejectAsync(id, body?.Reason ?? string.Empty, Actor(), cancellationToken).ConfigureAwait(false));

    private IActionResult Respond(QuoteResult result) => result.Outcome switch
    {
        QuoteOutcome.Done => NoContent(),
        QuoteOutcome.NotFound => NotFound(),

        // Not a malformed request: a request this person may not make. The same body from the
        // Owner succeeds, and a 422 would send the salesperson looking for a typo (BR-SALE-04).
        QuoteOutcome.NeedsApproval => Problem(
            title: "That discount needs approval",
            detail: result.Message,
            statusCode: StatusCodes.Status403Forbidden,
            type: ErrorType(result.Code!),
            extensions: Extensions(result.Code!, result.Field)),

        QuoteOutcome.Conflict => Problem(
            title: "The quote has moved on",
            detail: result.Message,
            statusCode: StatusCodes.Status409Conflict,
            type: ErrorType(result.Code!),
            extensions: Extensions(result.Code!, result.Field)),

        _ => Problem(
            title: "That will not do",
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

        if (string.Equals(code, "APPROVAL_REQUIRED", StringComparison.Ordinal))
        {
            extensions["thresholdPercent"] = QuoteLineItem.DiscountApprovalThreshold * 100m;
        }

        return extensions;
    }

    /// <summary>
    /// Who is asking, and whether they may approve a large discount. The identity travels with it
    /// so the approval can be recorded on the quote rather than merely allowed.
    /// </summary>
    private SalesActor SalesUser()
    {
        var id = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;

        // The role, not a permission. AZ-43 gives Sales and the Owner the same
        // `sales.quote.discount` permission and distinguishes them by limit - `Y-LIMIT(15)` against
        // `Y` - so the limit is a property of the role and inventing a second permission for it
        // would be adding to a frozen catalogue.
        return new SalesActor(Actor(), id, User.IsInRole(RoleNames.Owner));
    }

    private string Actor() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

public sealed record CreateQuoteBody(Guid OrganisationId, Guid ContactId, Guid? LeadId, string? Notes);

public sealed record QuoteLineBody(
    Guid ProductId,
    Guid? PricingPlanId,
    string Description,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal? TaxRatePercent);

public sealed record SendQuoteBody(DateOnly? ValidUntil);

public sealed record RejectQuoteBody(string? Reason);

public sealed record QuoteSummary(
    Guid Id,
    string QuoteNumber,
    string Organisation,
    string Status,
    string Currency,
    decimal GrandTotal,
    DateOnly? IssuedOn,
    DateOnly? ValidUntil,
    int LineCount);

public sealed record QuoteLineRow(
    Guid Id,
    Guid ProductId,
    Guid? PricingPlanId,
    string Description,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxRatePercent,
    decimal LineTotal,
    decimal TaxAmount,
    int SortOrder);

public sealed record QuoteDetail(
    Guid Id,
    string QuoteNumber,
    Guid OrganisationId,
    string Organisation,
    Guid ContactId,
    string ContactName,
    string ContactEmail,
    string Status,
    string Currency,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    DateOnly? IssuedOn,
    DateOnly? ValidUntil,
    string? Notes,
    string? RejectReason,
    Guid? RevisionOfQuoteId,
    bool IsEditable,
    IReadOnlyList<QuoteLineRow> Lines);
