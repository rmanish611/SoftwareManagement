using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Crm;

namespace SoftwareManagement.Domain.Sales;

/// <summary>
/// A bill (E-39).
///
/// The figures are stored, not recomputed on read, because an invoice is a statement made on a
/// date: changing a plan's price afterwards must not change what was invoiced. The outstanding
/// amount is derived, because that one does move - every payment changes it, and a stored copy is
/// a second source of truth for the number that matters most.
/// </summary>
public class Invoice : AuditableEntity
{
    /// <summary>The tax rate applied when nothing else is specified. GST at eighteen percent.</summary>
    public const decimal DefaultTaxRatePercent = 18.00m;

    /// <summary>Overdue the day after the due date; at these ages the subscription moves (BR-SALE-08).</summary>
    public const int PastDueAfterDays = 7;

    public const int SuspendAfterDays = 21;

    public string InvoiceNumber { get; set; } = string.Empty;

    public Guid? SubscriptionId { get; set; }

    public Subscription? Subscription { get; set; }

    public Guid OrganisationId { get; set; }

    public Organisation? Organisation { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public DateOnly? IssuedOn { get; set; }

    public DateOnly? DueDate { get; set; }

    /// <summary>The period this bill covers, which is what a customer checks first.</summary>
    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Currency { get; set; } = "INR";

    public decimal TaxRatePercent { get; set; } = DefaultTaxRatePercent;

    public decimal SubTotal { get; set; }

    public decimal TaxTotal { get; set; }

    public decimal GrandTotal { get; set; }

    public decimal AmountPaid { get; set; }

    public string? Notes { get; set; }

    public ICollection<Payment> Payments { get; } = [];

    /// <summary>
    /// What is still owed. Derived rather than stored: every payment changes it, and a stored copy
    /// would be a second source of truth for the one number a customer argues about.
    /// </summary>
    public decimal Outstanding() => QuoteLineItem.Round(GrandTotal - AmountPaid);

    /// <summary>
    /// Sets the figures from an amount before tax, rounding the tax the way every other line is
    /// rounded (BR-SALE-03).
    /// </summary>
    public void SetAmount(decimal subTotal)
    {
        SubTotal = QuoteLineItem.Round(subTotal);
        TaxTotal = QuoteLineItem.Round(SubTotal * TaxRatePercent / 100m);
        GrandTotal = SubTotal + TaxTotal;
    }

    /// <summary>
    /// Whether this invoice is chasing money. A draft has not been sent and a cancelled one is not
    /// owed, so neither can be overdue however old it is.
    /// </summary>
    public bool IsChaseable() =>
        Status is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid or InvoiceStatus.Overdue;
}

public enum InvoiceStatus
{
    Draft = 0,
    Issued = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Overdue = 4,
    Cancelled = 5,
}

/// <summary>
/// Money received (E-40). Append-only: a payment that turns out to be wrong is corrected by
/// recording a refund, never by editing the original, because the bank statement it reconciles
/// against cannot be edited either.
/// </summary>
public class Payment : AuditableEntity
{
    public Guid InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    public decimal Amount { get; set; }

    public PaymentMode Mode { get; set; }

    /// <summary>The UTR, cheque number or transaction id. Unique per invoice, so the same transfer
    /// cannot be entered twice by two people reconciling the same statement (EX-226).</summary>
    public string ReferenceNumber { get; set; } = string.Empty;

    public DateOnly ReceivedOn { get; set; }

    public Guid RecordedByUserId { get; set; }

    public bool IsRefund { get; set; }

    public string? Notes { get; set; }
}

public enum PaymentMode
{
    Upi = 0,
    NeftRtgs = 1,
    Cheque = 2,
    Cash = 3,
    CardLink = 4,
    Other = 5,
}
