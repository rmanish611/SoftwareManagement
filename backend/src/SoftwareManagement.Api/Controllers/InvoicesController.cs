using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Sales;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Sales;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// Invoices and the money against them.
///
/// The register is ordered by invoice number rather than by date, because that is how an auditor
/// reads it: a gap in the sequence is the thing they are looking for, and a date order hides it
/// (REQ-RPT-007).
/// </summary>
[ApiController]
[Route("api/v1/invoices")]
public sealed class InvoicesController(AppDbContext dbContext, IBillingService billing) : ControllerBase
{
    private const int MaxPageSize = 200;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly IBillingService _billing = billing;

    [HttpGet]
    [Authorize(Policy = Permissions.Finance.InvoiceRead)]
    public async Task<ActionResult<IReadOnlyList<InvoiceRow>>> List(
        [FromQuery] string? status,
        [FromQuery] Guid? organisationId,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = _dbContext.Invoices.AsNoTracking();

        if (Enum.TryParse<InvoiceStatus>(status, ignoreCase: true, out var wanted))
        {
            query = query.Where(i => i.Status == wanted);
        }

        if (organisationId is { } id)
        {
            query = query.Where(i => i.OrganisationId == id);
        }

        var rows = await query
            .OrderBy(i => i.InvoiceNumber)
            .Take(take)
            .Select(i => new InvoiceRow(
                i.Id,
                i.InvoiceNumber,
                i.Organisation!.DisplayName,
                i.Status.ToString(),
                i.Currency,
                i.SubTotal,
                i.TaxTotal,
                i.GrandTotal,
                i.AmountPaid,
                i.GrandTotal - i.AmountPaid,
                i.IssuedOn,
                i.DueDate))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Finance.InvoiceRead)]
    public async Task<ActionResult<InvoiceDetail>> Get(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await _dbContext.Invoices
            .AsNoTracking()
            .Include(i => i.Organisation)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false);

        if (invoice is null)
        {
            return NotFound();
        }

        return Ok(new InvoiceDetail(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.SubscriptionId,
            invoice.OrganisationId,
            invoice.Organisation!.DisplayName,
            invoice.Organisation.Gstin,
            invoice.Status.ToString(),
            invoice.Description,
            invoice.Currency,
            invoice.TaxRatePercent,
            invoice.SubTotal,
            invoice.TaxTotal,
            invoice.GrandTotal,
            invoice.AmountPaid,
            invoice.Outstanding(),
            invoice.IssuedOn,
            invoice.DueDate,
            invoice.PeriodStart,
            invoice.PeriodEnd,
            invoice.Notes,
            [.. invoice.Payments
                .OrderBy(p => p.ReceivedOn)
                .Select(p => new PaymentRow(p.Id, p.Amount, p.Mode.ToString(), p.ReferenceNumber, p.ReceivedOn, p.IsRefund, p.Notes))]));
    }

    [HttpPost("{id:guid}/payments")]
    [Authorize(Policy = Permissions.Finance.PaymentWrite)]
    public async Task<IActionResult> RecordPayment(Guid id, PaymentBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!Enum.TryParse<PaymentMode>(body.Mode, ignoreCase: true, out var mode))
        {
            return Problem(
                title: "That is not a payment method",
                detail: "The mode must be one of UPI, NEFT/RTGS, cheque, cash, a card link, or other.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: BillingErrors.Type("UNKNOWN_PAYMENT_MODE"),
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["code"] = "UNKNOWN_PAYMENT_MODE",
                    ["field"] = "mode",
                });
        }

        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;

        var result = await _billing.RecordPaymentAsync(
            id,
            new NewPayment(
                body.Amount,
                mode,
                body.ReferenceNumber,
                body.ReceivedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
                userId,
                body.Notes),
            User.FindFirstValue(ClaimTypes.Email) ?? "unknown",
            cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            BillingOutcome.Done => Ok(new { invoiceNumber = result.Reference, outstanding = result.Amount }),
            BillingOutcome.NotFound => NotFound(),
            BillingOutcome.Conflict => Problem(
                title: "That payment is already recorded",
                detail: result.Message,
                statusCode: StatusCodes.Status409Conflict,
                type: BillingErrors.Type(result.Code!),
                extensions: BillingErrors.Extensions(result)),
            _ => Problem(
                title: "That will not do",
                detail: result.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: BillingErrors.Type(result.Code!),
                extensions: BillingErrors.Extensions(result)),
        };
    }
}

public sealed record PaymentBody(decimal Amount, string Mode, string ReferenceNumber, DateOnly? ReceivedOn, string? Notes);

public sealed record PaymentRow(
    Guid Id,
    decimal Amount,
    string Mode,
    string ReferenceNumber,
    DateOnly ReceivedOn,
    bool IsRefund,
    string? Notes);

public sealed record InvoiceRow(
    Guid Id,
    string InvoiceNumber,
    string Organisation,
    string Status,
    string Currency,
    decimal SubTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal Outstanding,
    DateOnly? IssuedOn,
    DateOnly? DueDate);

public sealed record InvoiceDetail(
    Guid Id,
    string InvoiceNumber,
    Guid? SubscriptionId,
    Guid OrganisationId,
    string Organisation,
    string? Gstin,
    string Status,
    string Description,
    string Currency,
    decimal TaxRatePercent,
    decimal SubTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal Outstanding,
    DateOnly? IssuedOn,
    DateOnly? DueDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? Notes,
    IReadOnlyList<PaymentRow> Payments);
