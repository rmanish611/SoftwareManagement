using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Crm;
using SoftwareManagement.Domain.Leads;

namespace SoftwareManagement.Domain.Sales;

/// <summary>
/// A price put in writing (E-34).
///
/// The totals are stored rather than computed on read, because a quote that was sent for one figure
/// must keep saying that figure even after a product's price changes. They are recomputed from the
/// lines on every edit while the quote is still a draft, and frozen the moment it is sent
/// (BR-SALE-05).
/// </summary>
public class Quote : AuditableEntity
{
    /// <summary>
    /// Gapless per financial year, `Q/<FY>/<00001>`, allocated inside the transaction that first
    /// saves the quote and never reused or renumbered (BR-SALE-01). A gap in this sequence is a
    /// question from an auditor that nobody can answer.
    /// </summary>
    public string QuoteNumber { get; set; } = string.Empty;

    public Guid OrganisationId { get; set; }

    public Organisation? Organisation { get; set; }

    public Guid ContactId { get; set; }

    public Contact? Contact { get; set; }

    public Guid? LeadId { get; set; }

    public Lead? Lead { get; set; }

    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;

    public string Currency { get; set; } = "INR";

    public decimal SubTotal { get; set; }

    public decimal DiscountTotal { get; set; }

    public decimal TaxTotal { get; set; }

    public decimal GrandTotal { get; set; }

    public DateOnly? IssuedOn { get; set; }

    public DateOnly? ValidUntil { get; set; }

    public string? Notes { get; set; }

    public string? RejectReason { get; set; }

    /// <summary>Set when this quote replaces one that had already been sent (BR-SALE-05).</summary>
    public Guid? RevisionOfQuoteId { get; set; }

    /// <summary>Who approved a discount above the threshold, when one was applied (BR-SALE-04).</summary>
    public Guid? ApprovedByUserId { get; set; }

    public ICollection<QuoteLineItem> Lines { get; } = [];

    /// <summary>
    /// Whether the figures may still change. Everything after Draft is a number somebody has been
    /// shown, and changing one of those silently is the failure BR-SALE-05 exists to prevent.
    /// </summary>
    public bool IsEditable() => Status == QuoteStatus.Draft;
}

public enum QuoteStatus
{
    Draft = 0,
    Sent = 1,
    Accepted = 2,
    Rejected = 3,
    Expired = 4,
    Withdrawn = 5,
}

/// <summary>
/// One priced line (E-35). The arithmetic lives in <see cref="Recalculate"/> rather than in the
/// service, so there is exactly one place where money is rounded.
/// </summary>
public class QuoteLineItem : AuditableEntity
{
    /// <summary>
    /// Above this share of the line, a discount needs the Owner (BR-SALE-04). Fifteen percent is
    /// the number in the rule; it is here so the rule and the check cannot drift apart.
    /// </summary>
    public const decimal DiscountApprovalThreshold = 0.15m;

    public Guid QuoteId { get; set; }

    public Quote? Quote { get; set; }

    public Guid ProductId { get; set; }

    public Product? Product { get; set; }

    public Guid? PricingPlanId { get; set; }

    public string Description { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;

    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TaxRatePercent { get; set; } = 18.00m;

    public decimal LineTotal { get; set; }

    public decimal TaxAmount { get; set; }

    public int SortOrder { get; set; }

    /// <summary>What the line costs before any discount.</summary>
    public decimal Subtotal() => Round(Quantity * UnitPrice);

    /// <summary>Whether this discount is large enough to need the Owner's approval.</summary>
    public bool NeedsApproval()
    {
        var subtotal = Subtotal();
        return subtotal > 0 && DiscountAmount > Round(subtotal * DiscountApprovalThreshold);
    }

    /// <summary>
    /// Rounds this line and computes its tax.
    ///
    /// Per line, never once at the end (BR-SALE-03). Summing unrounded lines and rounding the
    /// total produces a figure that does not match the lines printed above it, and the customer
    /// who adds them up is the one who notices.
    /// </summary>
    public void Recalculate()
    {
        LineTotal = Subtotal() - Round(DiscountAmount);
        TaxAmount = Round(LineTotal * TaxRatePercent / 100m);
    }

    /// <summary>
    /// Two decimal places, half away from zero.
    ///
    /// .NET rounds half to even by default, so 0.125 would become 0.12. Money is not rounded that
    /// way anywhere a customer can see it, and an invoice that disagrees with a hand calculator by
    /// a paisa is an invoice somebody queries.
    /// </summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
