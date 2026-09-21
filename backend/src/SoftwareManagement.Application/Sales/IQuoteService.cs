namespace SoftwareManagement.Application.Sales;

/// <summary>
/// Allocates the next gapless document number for a financial year.
/// </summary>
public interface INumberAllocator
{
    Task<string> NextAsync(string key, string prefix, DateOnly issuedOn, CancellationToken cancellationToken);
}

/// <summary>
/// Quoting: the numbers a customer is shown, and the rules about who may change them.
///
/// Like the lead pipeline, every operation returns an outcome rather than throwing, because each
/// has a refusal the caller must turn into a particular status code - and the discount refusal in
/// particular is a 403 rather than a 422, because it is about who is asking and not about what
/// they sent (BR-SALE-04).
/// </summary>
public interface IQuoteService
{
    Task<QuoteResult> CreateAsync(NewQuote quote, string actor, CancellationToken cancellationToken);

    Task<QuoteResult> AddLineAsync(Guid quoteId, NewQuoteLine line, SalesActor actor, CancellationToken cancellationToken);

    Task<QuoteResult> RemoveLineAsync(Guid quoteId, Guid lineId, string actor, CancellationToken cancellationToken);

    Task<QuoteResult> SendAsync(Guid quoteId, DateOnly? validUntil, string actor, CancellationToken cancellationToken);

    /// <summary>
    /// Copies a sent quote into a new draft that points back at it. The original is withdrawn
    /// rather than edited, because a number somebody has already been shown must keep meaning what
    /// it meant (BR-SALE-05).
    /// </summary>
    Task<QuoteResult> ReviseAsync(Guid quoteId, string actor, CancellationToken cancellationToken);

    Task<QuoteResult> AcceptAsync(Guid quoteId, string actor, CancellationToken cancellationToken);

    Task<QuoteResult> RejectAsync(Guid quoteId, string reason, string actor, CancellationToken cancellationToken);

    /// <summary>Moves quotes past their validity to Expired. Returns how many moved (BR-SALE-02).</summary>
    Task<int> ExpireDueAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Who is asking, for the one rule that depends on it. Carrying the identity rather than a boolean
/// means the approving user can be recorded on the quote, which is what makes a large discount
/// auditable instead of merely permitted.
/// </summary>
public sealed record SalesActor(string Email, Guid UserId, bool CanApproveDiscount);

public sealed record NewQuote(Guid OrganisationId, Guid ContactId, Guid? LeadId, string? Notes);

public sealed record NewQuoteLine(
    Guid ProductId,
    Guid? PricingPlanId,
    string Description,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxRatePercent);

public enum QuoteOutcome
{
    Done = 0,
    NotFound = 1,
    Refused = 2,
    Conflict = 3,
    NeedsApproval = 4,
}

public sealed record QuoteResult(
    QuoteOutcome Outcome,
    string? Code = null,
    string? Field = null,
    string? Message = null,
    Guid? QuoteId = null,
    string? QuoteNumber = null)
{
    public static QuoteResult Done(Guid quoteId, string? quoteNumber = null) =>
        new(QuoteOutcome.Done, QuoteId: quoteId, QuoteNumber: quoteNumber);

    public static QuoteResult NotFound() => new(QuoteOutcome.NotFound);

    public static QuoteResult Refused(string code, string? field, string message) =>
        new(QuoteOutcome.Refused, code, field, message);

    public static QuoteResult Conflict(string code, string message, Guid? quoteId = null) =>
        new(QuoteOutcome.Conflict, code, null, message, quoteId);

    public static QuoteResult NeedsApproval(string message) =>
        new(QuoteOutcome.NeedsApproval, "APPROVAL_REQUIRED", "discountAmount", message);
}
