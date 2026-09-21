using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Crm;

namespace SoftwareManagement.Domain.Sales;

/// <summary>
/// A running instance of one of our products, sold to one customer (E-36).
///
/// The tenant is the thing that exists in the world - a URL somebody logs into - and the
/// subscription beside it is the money. They are separate because one can outlive the other: a
/// suspended subscription leaves the tenant standing, and an ended tenant may still have an unpaid
/// invoice against it.
/// </summary>
public class Tenant : AuditableEntity
{
    public Guid OrganisationId { get; set; }

    public Organisation? Organisation { get; set; }

    public Guid ProductId { get; set; }

    public Product? Product { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? EnvironmentUrl { get; set; }

    public TenantEnvironment Environment { get; set; } = TenantEnvironment.Production;

    public DateOnly? ProvisionedOn { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Requested;

    public string? Notes { get; set; }
}

public enum TenantEnvironment
{
    Trial = 0,
    Staging = 1,
    Production = 2,
}

public enum TenantStatus
{
    Requested = 0,
    Provisioning = 1,
    Active = 2,
    Suspended = 3,
    Ended = 4,
}

/// <summary>
/// What the customer pays, and when (E-37).
///
/// One per tenant, enforced by a unique index: two subscriptions against one instance would bill
/// the same thing twice, and no report could say which was real.
/// </summary>
public class Subscription : AuditableEntity
{
    /// <summary>A trial is exactly a fortnight unless the owner says otherwise (BR-SALE-07).</summary>
    public const int TrialDays = 14;

    /// <summary>An invoice is due this many days after it is issued.</summary>
    public const int PaymentTermDays = 14;

    public Guid TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    public Guid PricingPlanId { get; set; }

    public PricingPlan? PricingPlan { get; set; }

    public Guid? QuoteId { get; set; }

    public Quote? Quote { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trial;

    public int Seats { get; set; } = 1;

    public decimal UnitPrice { get; set; }

    public string Currency { get; set; } = "INR";

    public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Monthly;

    public DateOnly StartedOn { get; set; }

    public DateOnly? TrialEndsOn { get; set; }

    public DateOnly CurrentPeriodEndsOn { get; set; }

    public DateOnly? CancelledOn { get; set; }

    public string? CancelReason { get; set; }

    /// <summary>
    /// The renewal date the customer has already been reminded about, so the fortnightly sweep
    /// reminds once per renewal rather than once per run (REQ-SALE-014).
    /// </summary>
    public DateOnly? RenewalReminderSentFor { get; set; }

    public ICollection<SubscriptionEvent> Events { get; } = [];

    /// <summary>What one period costs before tax, rounded the way every other figure is.</summary>
    public decimal PeriodAmount() => QuoteLineItem.Round(Seats * UnitPrice);

    /// <summary>
    /// Whether this subscription counts towards recurring revenue. A cancelled or expired one does
    /// not, and neither does a trial nobody has paid for yet.
    /// </summary>
    public bool IsBillable() => Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue;

    public DateOnly NextPeriodEnd(DateOnly from) => BillingPeriod switch
    {
        BillingPeriod.Yearly => from.AddYears(1),
        BillingPeriod.Quarterly => from.AddMonths(3),
        _ => from.AddMonths(1),
    };
}

public enum SubscriptionStatus
{
    Trial = 0,
    Active = 1,
    PastDue = 2,
    Suspended = 3,
    Cancelled = 4,
    Expired = 5,
}

/// <summary>
/// Every state change, appended (E-38).
///
/// A status column says where a subscription is; only this says how it got there. When a customer
/// asks why they were suspended, the answer has to be a row somebody wrote at the time
/// (REQ-SALE-010).
/// </summary>
public class SubscriptionEvent : AuditableEntity
{
    public Guid SubscriptionId { get; set; }

    public Subscription? Subscription { get; set; }

    public SubscriptionStatus? FromStatus { get; set; }

    public SubscriptionStatus ToStatus { get; set; }

    public string? Reason { get; set; }

    public DateOnly EffectiveOn { get; set; }

    public string TriggeredBy { get; set; } = string.Empty;
}
