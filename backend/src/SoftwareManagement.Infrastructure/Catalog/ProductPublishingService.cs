using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Catalog;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Catalog;

/// <summary>
/// Publishing a product, and retiring one.
///
/// The rule this enforces is BR-CAT-01: a product reaches the public only with a published
/// category, three features, a screenshot and a plan. It is not a formality. A product page with
/// one bullet point and no price tells a buyer the company is not serious, and it is the page they
/// judge the company by.
/// </summary>
public sealed class ProductPublishingService(
    AppDbContext dbContext,
    ISlugService slugs,
    IClock clock) : IProductPublishingService
{
    private const string ProductPathPrefix = "/products/";

    private readonly AppDbContext _dbContext = dbContext;
    private readonly ISlugService _slugs = slugs;
    private readonly IClock _clock = clock;

    public async Task<ProductPublishResult> PublishAsync(Guid productId, string actorEmail, CancellationToken cancellationToken)
    {
        var product = await LoadAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return ProductPublishResult.Failed(ProductPublishOutcome.NotFound);
        }

        if (product.Status == ContentStatus.Archived)
        {
            return ProductPublishResult.Failed(ProductPublishOutcome.AlreadyArchived);
        }

        var readiness = Evaluate(product);
        if (!readiness.IsReady)
        {
            return ProductPublishResult.NotReady(readiness);
        }

        product.Status = ContentStatus.Published;
        product.PublishedAtUtc = _clock.UtcNow;
        product.PublishAtUtc = null;
        product.ModifiedBy = actorEmail;

        // The snapshot is written before the status change is saved, so what the public reads is
        // the state that was approved rather than whatever the draft becomes next.
        await WriteVersionAsync(product, actorEmail, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ProductPublishResult.Ok(ProductPublishOutcome.Published);
    }

    public async Task<ProductPublishResult> UnpublishAsync(Guid productId, string actorEmail, CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false);

        if (product is null)
        {
            return ProductPublishResult.Failed(ProductPublishOutcome.NotFound);
        }

        product.Status = ContentStatus.Unpublished;
        product.ModifiedBy = actorEmail;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ProductPublishResult.Ok(ProductPublishOutcome.Unpublished);
    }

    public async Task<ProductPublishResult> ArchiveAsync(Guid productId, string actorEmail, CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false);

        if (product is null)
        {
            return ProductPublishResult.Failed(ProductPublishOutcome.NotFound);
        }

        if (product.Status == ContentStatus.Archived)
        {
            return ProductPublishResult.Failed(ProductPublishOutcome.AlreadyArchived);
        }

        var wasLive = product.Status is ContentStatus.Published or ContentStatus.Modified;

        product.Status = ContentStatus.Archived;
        product.ModifiedBy = actorEmail;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The address of a product that was live stays claimed forever. Someone has it bookmarked,
        // a search engine has it indexed, and a quote may cite it; handing it to a different
        // product later would silently redirect all of that to the wrong thing (BR-CAT-07).
        if (wasLive)
        {
            await _slugs.RecordRenameAsync(
                ProductPathPrefix + product.Slug,
                "/products",
                cancellationToken).ConfigureAwait(false);
        }

        return ProductPublishResult.Ok(ProductPublishOutcome.Archived);
    }

    public async Task<ProductReadinessResult?> ReadinessAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await LoadAsync(productId, cancellationToken).ConfigureAwait(false);
        return product is null ? null : Evaluate(product);
    }

    private static ProductReadinessResult Evaluate(Product product) =>
        ProductReadiness.Evaluate(
            product.Features.Count,
            product.Screenshots.Count,
            product.Plans.Count(p => p.IsPublished),
            product.Category?.IsPublished ?? false);

    private Task<Product?> LoadAsync(Guid productId, CancellationToken cancellationToken) =>
        _dbContext.Products
            .Include(p => p.Category)
            .Include(p => p.Features)
            .Include(p => p.Screenshots)
            .Include(p => p.Plans)
            .Include(p => p.Seo)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

    private async Task WriteVersionAsync(Product product, string actorEmail, CancellationToken cancellationToken)
    {
        var next = await _dbContext.ContentVersions
            .Where(v => v.EntityType == nameof(Product) && v.EntityId == product.Id)
            .MaxAsync(v => (int?)v.VersionNumber, cancellationToken).ConfigureAwait(false) ?? 0;

        var snapshot = new ProductSnapshot(
            product.Name,
            product.Slug,
            product.Tagline,
            product.Summary,
            product.Body,
            [.. product.Features.OrderBy(f => f.SortOrder).Select(f => f.Name)],
            [.. product.Plans.Where(p => p.IsPublished).OrderBy(p => p.SortOrder).Select(p => $"{p.Name} {p.Currency} {p.Price}")]);

        _dbContext.ContentVersions.Add(new ContentVersion
        {
            Id = Guid.NewGuid(),
            EntityType = nameof(Product),
            EntityId = product.Id,
            VersionNumber = next + 1,
            SnapshotJson = JsonSerializer.Serialize(snapshot),
            CreatedAtUtc = _clock.UtcNow,
            CreatedBy = actorEmail,
        });

        // History is capped rather than unbounded, and the cap is the same as for pages so a
        // reader of the versions table does not have to remember two numbers.
        var surplus = await _dbContext.ContentVersions
            .Where(v => v.EntityType == nameof(Product) && v.EntityId == product.Id)
            .OrderByDescending(v => v.VersionNumber)
            .Skip(ContentVersion.RetainedPerItem - 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (surplus.Count > 0)
        {
            _dbContext.ContentVersions.RemoveRange(surplus);
        }
    }
}

/// <summary>What a product looked like at the moment it was published.</summary>
public sealed record ProductSnapshot(
    string Name,
    string Slug,
    string Tagline,
    string Summary,
    string? Body,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Plans);
