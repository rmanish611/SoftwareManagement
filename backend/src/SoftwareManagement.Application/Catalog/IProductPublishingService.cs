using SoftwareManagement.Domain.Catalog;

namespace SoftwareManagement.Application.Catalog;

/// <summary>
/// Publishing, unpublishing and archiving a product.
///
/// It is separate from the page publishing service because the rule that matters for a product is a
/// different one: a page is blocked by what it links to, a product by whether it has enough of a
/// page to be worth reading (BR-CAT-01).
/// </summary>
public interface IProductPublishingService
{
    Task<ProductPublishResult> PublishAsync(Guid productId, string actorEmail, CancellationToken cancellationToken);

    Task<ProductPublishResult> UnpublishAsync(Guid productId, string actorEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Retires a product without losing it: it leaves the public catalogue, its history stays, and
    /// its address is never reissued to something else (BR-CAT-07, REQ-CAT-015).
    /// </summary>
    Task<ProductPublishResult> ArchiveAsync(Guid productId, string actorEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the product could be published right now, and what is missing if not. The admin
    /// screen asks this so an editor sees the answer before pressing the button.
    /// </summary>
    Task<ProductReadinessResult?> ReadinessAsync(Guid productId, CancellationToken cancellationToken);
}

public sealed record ProductPublishResult(ProductPublishOutcome Outcome, ProductReadinessResult? Readiness)
{
    public bool Succeeded => Outcome is ProductPublishOutcome.Published
        or ProductPublishOutcome.Unpublished
        or ProductPublishOutcome.Archived;

    public static ProductPublishResult Ok(ProductPublishOutcome outcome) => new(outcome, null);

    public static ProductPublishResult NotReady(ProductReadinessResult readiness) =>
        new(ProductPublishOutcome.NotReady, readiness);

    public static ProductPublishResult Failed(ProductPublishOutcome outcome) => new(outcome, null);
}

public enum ProductPublishOutcome
{
    Published = 0,
    Unpublished = 1,
    Archived = 2,
    NotFound = 3,
    NotReady = 4,
    AlreadyArchived = 5,
}
