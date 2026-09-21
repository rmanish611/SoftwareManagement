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
/// What is running, for whom, and on what terms.
/// </summary>
[ApiController]
[Route("api/v1/subscriptions")]
public sealed class SubscriptionsController(AppDbContext dbContext, IBillingService billing) : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly IBillingService _billing = billing;

    [HttpGet]
    [Authorize(Policy = Permissions.Sales.SubscriptionRead)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionSummary>>> List(
        [FromQuery] string? status,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = _dbContext.Subscriptions.AsNoTracking();

        if (Enum.TryParse<SubscriptionStatus>(status, ignoreCase: true, out var wanted))
        {
            query = query.Where(s => s.Status == wanted);
        }

        var rows = await query
            // Renewing soonest first: the screen exists to stop a customer lapsing by accident.
            .OrderBy(s => s.CurrentPeriodEndsOn)
            .Take(take)
            .Select(s => new SubscriptionSummary(
                s.Id,
                s.TenantId,
                s.Tenant!.Name,
                s.Tenant.Organisation!.DisplayName,
                s.Tenant.Product!.Name,
                s.PricingPlan!.Name,
                s.Status.ToString(),
                s.Seats,
                s.UnitPrice,
                s.Currency,
                s.BillingPeriod.ToString(),
                s.StartedOn,
                s.TrialEndsOn,
                s.CurrentPeriodEndsOn))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Sales.SubscriptionRead)]
    public async Task<ActionResult<SubscriptionDetail>> Get(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await _dbContext.Subscriptions
            .AsNoTracking()
            .Include(s => s.Tenant!).ThenInclude(t => t.Organisation)
            .Include(s => s.Tenant!).ThenInclude(t => t.Product)
            .Include(s => s.PricingPlan)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);

        if (subscription is null)
        {
            return NotFound();
        }

        var events = await _dbContext.SubscriptionEvents
            .AsNoTracking()
            .Where(e => e.SubscriptionId == id)
            .OrderByDescending(e => e.EffectiveOn).ThenByDescending(e => e.CreatedAtUtc)
            .Select(e => new SubscriptionEventRow(
                e.Id,
                e.FromStatus == null ? null : e.FromStatus.ToString(),
                e.ToStatus.ToString(),
                e.Reason,
                e.EffectiveOn,
                e.TriggeredBy))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var invoices = await _dbContext.Invoices
            .AsNoTracking()
            .Where(i => i.SubscriptionId == id)
            .OrderByDescending(i => i.IssuedOn)
            .Select(i => new InvoiceSummary(
                i.Id,
                i.InvoiceNumber,
                i.Status.ToString(),
                i.Currency,
                i.GrandTotal,
                i.AmountPaid,
                i.IssuedOn,
                i.DueDate))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new SubscriptionDetail(
            subscription.Id,
            subscription.TenantId,
            subscription.Tenant!.Name,
            subscription.Tenant.EnvironmentUrl,
            subscription.Tenant.Environment.ToString(),
            subscription.Tenant.Status.ToString(),
            subscription.Tenant.Organisation!.DisplayName,
            subscription.Tenant.Product!.Name,
            subscription.PricingPlan!.Name,
            subscription.Status.ToString(),
            subscription.Seats,
            subscription.UnitPrice,
            subscription.Currency,
            subscription.BillingPeriod.ToString(),
            subscription.StartedOn,
            subscription.TrialEndsOn,
            subscription.CurrentPeriodEndsOn,
            subscription.CancelledOn,
            subscription.CancelReason,
            events,
            invoices));
    }

    /// <summary>Turns an accepted quote into a tenant and a subscription (REQ-SALE-007).</summary>
    [HttpPost("from-quote/{quoteId:guid}")]
    [Authorize(Policy = Permissions.Sales.TenantWrite)]
    public async Task<IActionResult> Provision(Guid quoteId, ProvisionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!Enum.TryParse<TenantEnvironment>(body.Environment, ignoreCase: true, out var environment))
        {
            environment = TenantEnvironment.Production;
        }

        var result = await _billing.ProvisionFromQuoteAsync(
            quoteId,
            new NewTenant(body.Name, body.EnvironmentUrl, environment, body.Notes),
            Actor(),
            cancellationToken).ConfigureAwait(false);

        return result.Outcome == BillingOutcome.Done
            ? Created($"/api/v1/subscriptions/{result.SubjectId}", new { id = result.SubjectId, tenant = result.Reference })
            : Respond(result);
    }

    [HttpPost("{id:guid}/trial")]
    [Authorize(Policy = Permissions.Sales.SubscriptionChange)]
    public async Task<IActionResult> StartTrial(Guid id, CancellationToken cancellationToken) =>
        Respond(await _billing.StartTrialAsync(id, Actor(), cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/invoices")]
    [Authorize(Policy = Permissions.Finance.InvoiceIssue)]
    public async Task<IActionResult> Issue(Guid id, CancellationToken cancellationToken)
    {
        var result = await _billing.IssueInvoiceAsync(id, Actor(), cancellationToken).ConfigureAwait(false);

        return result.Outcome == BillingOutcome.Done
            ? Created($"/api/v1/invoices/{result.SubjectId}", new { id = result.SubjectId, invoiceNumber = result.Reference, grandTotal = result.Amount })
            : Respond(result);
    }

    [HttpPost("{id:guid}/plan")]
    [Authorize(Policy = Permissions.Sales.SubscriptionChange)]
    public async Task<IActionResult> ChangePlan(Guid id, PlanChangeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var result = await _billing.ChangePlanAsync(
            id, new PlanChange(body.PricingPlanId, body.Seats, body.UnitPrice), Actor(), cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome == BillingOutcome.Done
            ? Ok(new { proratedCharge = result.Amount, invoiceNumber = result.Reference })
            : Respond(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Permissions.Sales.SubscriptionCancel)]
    public async Task<IActionResult> Cancel(Guid id, CancelBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Respond(await _billing.CancelAsync(id, body.Reason ?? string.Empty, Actor(), cancellationToken).ConfigureAwait(false));
    }

    private IActionResult Respond(BillingResult result) => result.Outcome switch
    {
        BillingOutcome.Done => NoContent(),
        BillingOutcome.NotFound => NotFound(),
        BillingOutcome.Conflict => Problem(
            title: "That cannot be done twice",
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

    private string Actor() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

/// <summary>Shared between the two billing controllers, so one code cannot mean two things.</summary>
internal static class BillingErrors
{
    public static string Type(string code) =>
        "https://softwaremanagement.example/errors/" + code.ToLowerInvariant().Replace('_', '-');

    public static Dictionary<string, object?> Extensions(BillingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = result.Code };

        if (result.Field is not null)
        {
            extensions["field"] = result.Field;
        }

        // The outstanding figure travels with an overpayment refusal, because the person is holding
        // a bank statement and needs to know what to enter instead (BR-SALE-09).
        if (result.Amount is { } amount)
        {
            extensions["outstanding"] = amount;
        }

        return extensions;
    }
}

public sealed record ProvisionBody(string Name, string? EnvironmentUrl, string? Environment, string? Notes);

public sealed record PlanChangeBody(Guid PricingPlanId, int Seats, decimal UnitPrice);

public sealed record CancelBody(string? Reason);

public sealed record SubscriptionSummary(
    Guid Id,
    Guid TenantId,
    string Tenant,
    string Organisation,
    string Product,
    string Plan,
    string Status,
    int Seats,
    decimal UnitPrice,
    string Currency,
    string BillingPeriod,
    DateOnly StartedOn,
    DateOnly? TrialEndsOn,
    DateOnly CurrentPeriodEndsOn);

public sealed record SubscriptionEventRow(
    Guid Id,
    string? FromStatus,
    string ToStatus,
    string? Reason,
    DateOnly EffectiveOn,
    string TriggeredBy);

public sealed record InvoiceSummary(
    Guid Id,
    string InvoiceNumber,
    string Status,
    string Currency,
    decimal GrandTotal,
    decimal AmountPaid,
    DateOnly? IssuedOn,
    DateOnly? DueDate);

public sealed record SubscriptionDetail(
    Guid Id,
    Guid TenantId,
    string Tenant,
    string? EnvironmentUrl,
    string Environment,
    string TenantStatus,
    string Organisation,
    string Product,
    string Plan,
    string Status,
    int Seats,
    decimal UnitPrice,
    string Currency,
    string BillingPeriod,
    DateOnly StartedOn,
    DateOnly? TrialEndsOn,
    DateOnly CurrentPeriodEndsOn,
    DateOnly? CancelledOn,
    string? CancelReason,
    IReadOnlyList<SubscriptionEventRow> Events,
    IReadOnlyList<InvoiceSummary> Invoices);
