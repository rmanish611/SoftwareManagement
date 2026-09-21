using SoftwareManagement.Domain.Sales;

namespace SoftwareManagement.Application.Sales;

/// <summary>
/// Tenants, subscriptions, invoices and the money against them.
///
/// Two operations here are deliberately idempotent, and for the same reason: accepting a quote and
/// recording a payment are both things two people can do at once from two screens. Accepting twice
/// must not produce two subscriptions (BR-SALE-06), and the same bank reference entered twice must
/// not be counted twice (EX-226).
/// </summary>
public interface IBillingService
{
    /// <summary>
    /// Turns an accepted quote into a tenant and a subscription. Called again for the same quote it
    /// returns what already exists rather than creating a second of either.
    /// </summary>
    Task<BillingResult> ProvisionFromQuoteAsync(Guid quoteId, NewTenant tenant, string actor, CancellationToken cancellationToken);

    Task<BillingResult> StartTrialAsync(Guid subscriptionId, string actor, CancellationToken cancellationToken);

    Task<BillingResult> IssueInvoiceAsync(Guid subscriptionId, string actor, CancellationToken cancellationToken);

    Task<BillingResult> RecordPaymentAsync(Guid invoiceId, NewPayment payment, string actor, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the plan or the seat count mid-term. An increase is charged for the remainder of the
    /// period; a decrease takes effect at renewal rather than refunding (BR-SALE-11).
    /// </summary>
    Task<BillingResult> ChangePlanAsync(Guid subscriptionId, PlanChange change, string actor, CancellationToken cancellationToken);

    Task<BillingResult> CancelAsync(Guid subscriptionId, string reason, string actor, CancellationToken cancellationToken);

    /// <summary>
    /// The scheduled half: expire finished trials, escalate unpaid invoices, and raise renewal
    /// invoices with their reminders. Takes a database lock, so a second instance cannot
    /// double-send (NFR-DEP-05).
    /// </summary>
    Task<BillingSweepResult> RunSweepAsync(CancellationToken cancellationToken);
}

public sealed record NewTenant(string Name, string? EnvironmentUrl, TenantEnvironment Environment, string? Notes);

public sealed record NewPayment(
    decimal Amount,
    PaymentMode Mode,
    string ReferenceNumber,
    DateOnly ReceivedOn,
    Guid RecordedByUserId,
    string? Notes);

public sealed record PlanChange(Guid PricingPlanId, int Seats, decimal UnitPrice);

public sealed record BillingSweepResult(
    int TrialsExpired,
    int MovedToPastDue,
    int Suspended,
    int RenewalsInvoiced,
    int DunningQueued,
    bool LockTaken);

public enum BillingOutcome
{
    Done = 0,
    NotFound = 1,
    Refused = 2,
    Conflict = 3,
}

public sealed record BillingResult(
    BillingOutcome Outcome,
    string? Code = null,
    string? Field = null,
    string? Message = null,
    Guid? SubjectId = null,
    string? Reference = null,
    decimal? Amount = null)
{
    public static BillingResult Done(Guid? subjectId = null, string? reference = null, decimal? amount = null) =>
        new(BillingOutcome.Done, SubjectId: subjectId, Reference: reference, Amount: amount);

    public static BillingResult NotFound() => new(BillingOutcome.NotFound);

    public static BillingResult Refused(string code, string? field, string message, decimal? amount = null) =>
        new(BillingOutcome.Refused, code, field, message, Amount: amount);

    public static BillingResult Conflict(string code, string message, Guid? subjectId = null) =>
        new(BillingOutcome.Conflict, code, null, message, subjectId);
}
