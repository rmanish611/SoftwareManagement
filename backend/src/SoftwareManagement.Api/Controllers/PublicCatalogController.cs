using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Analytics;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// The catalogue as a visitor sees it: published products only, grouped by the industry they serve.
///
/// Nothing here reveals a draft. A product that is not published is not in the list and its address
/// answers 404, not 403: telling an anonymous visitor that a hidden page exists is itself a leak
/// (NFR-AUTHZ-04).
/// </summary>
[ApiController]
[Route("api/v1/public")]
[AllowAnonymous]
public sealed class PublicCatalogController(
    AppDbContext dbContext,
    IPageViewCounter pageViews) : ControllerBase
{
    /// <summary>
    /// The most product cards one request will return, however many are asked for (NFR-PERF-04). A
    /// caller asking for a hundred thousand gets a hundred, not an error and not a slow query.
    /// </summary>
    public const int MaxPageSize = 100;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly IPageViewCounter _pageViews = pageViews;

    [HttpGet("catalog/categories")]
    public async Task<ActionResult<IReadOnlyList<PublicCategory>>> Categories(CancellationToken cancellationToken)
    {
        var categories = await _dbContext.ProductCategories
            .AsNoTracking()
            .Where(c => c.IsPublished)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new PublicCategory(
                c.Name,
                c.Slug,
                c.Description,
                c.IconKey,
                c.Products.Count(p => p.Status == ContentStatus.Published || p.Status == ContentStatus.Modified)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(categories);
    }

    /// <summary>
    /// The product cards, optionally filtered by industry and by a search term.
    ///
    /// An unknown category or a term that matches nothing returns an empty list rather than an
    /// error: a stale link from an old newsletter should show "nothing here yet" with a way to get
    /// in touch, not a failure page (REQ-CAT-012).
    /// </summary>
    [HttpGet("products")]
    [HttpGet("catalog/products")]
    public async Task<ActionResult<IReadOnlyList<PublicProductCard>>> Products(
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] int pageSize = 24,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _dbContext.Products
            .AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published || p.Status == ContentStatus.Modified);

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(p => p.Category!.Slug == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            // The feature text is searched as well as the name and the summary, because a buyer
            // looks for the thing they need done rather than for a product name (S-10).
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{term}%")
                || EF.Functions.Like(p.Tagline, $"%{term}%")
                || EF.Functions.Like(p.Summary, $"%{term}%")
                || p.Features.Any(f => EF.Functions.Like(f.Name, $"%{term}%")
                    || (f.Description != null && EF.Functions.Like(f.Description, $"%{term}%"))));
        }

        var cards = await query
            .OrderByDescending(p => p.IsFeatured).ThenBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Take(take)
            .Select(p => new PublicProductCard(
                p.Name,
                p.Slug,
                p.Tagline,
                p.Category!.Name,
                p.Category.Slug,
                p.IsFeatured,

                // "from Rs. X" is the cheapest published paid plan, never a free tier: quoting the
                // free plan as the starting price would misrepresent what the product costs
                // (BR-CAT-05).
                p.Plans
                    .Where(plan => plan.IsPublished && !plan.IsFreeTier && plan.Price > 0)
                    .Select(plan => (decimal?)plan.Price)
                    .Min(),
                p.Plans
                    .Where(plan => plan.IsPublished && !plan.IsFreeTier && plan.Price > 0)
                    .Select(plan => plan.Currency)
                    .FirstOrDefault(),
                p.Plans.Any(plan => plan.IsPublished && plan.IsFreeTier),
                p.Demo != null && p.Demo.IsEnabled && p.Demo.HealthState != DemoHealth.Down))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        await CountViewAsync("/products", cancellationToken).ConfigureAwait(false);
        return Ok(cards);
    }

    /// <summary>
    /// One product page: everything a visitor needs to judge whether it fits their business.
    /// </summary>
    [HttpGet("products/{slug}")]
    public async Task<ActionResult<PublicProductPage>> Product(string slug, CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Features)
            .Include(p => p.Screenshots).ThenInclude(s => s.MediaAsset)
            .Include(p => p.Plans).ThenInclude(p => p.PlanFeatures)
            .Include(p => p.Faqs)
            .Include(p => p.Demo)
            .Include(p => p.Seo)
            .FirstOrDefaultAsync(
                p => p.Slug == slug && (p.Status == ContentStatus.Published || p.Status == ContentStatus.Modified),
                cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            // A draft, an archived product and a slug nobody ever used are the same answer. Any
            // difference between them would tell a stranger what exists behind the sign-in.
            var redirect = await _dbContext.Redirects
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.FromPath == "/products/" + slug, cancellationToken)
                .ConfigureAwait(false);

            return redirect is null
                ? NotFound()
                : RedirectPermanent(redirect.ToPath);
        }

        var page = PublicProductPage.From(product);

        await CountViewAsync("/products/" + product.Slug, cancellationToken).ConfigureAwait(false);
        return Ok(page);
    }

    /// <summary>
    /// Serves an uploaded image to a visitor.
    ///
    /// The key is looked up in the media library rather than joined onto a directory, so a request
    /// carrying <c>../</c> or an absolute path finds no row and gets a 404: the filesystem is never
    /// asked about a path the database has not vouched for.
    /// </summary>
    [HttpGet("media/{storageKey}")]
    public async Task<IActionResult> Media(
        string storageKey,
        [FromServices] Application.Content.IFileStorage storage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storage);

        var asset = await _dbContext.MediaAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.StorageKey == storageKey || m.WebStorageKey == storageKey || m.ThumbnailStorageKey == storageKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (asset is null)
        {
            return NotFound();
        }

        var content = await storage.OpenAsync(storageKey, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return NotFound();
        }

        // Uploaded bytes are immutable: a new upload gets a new key, so this may be cached hard.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(content, asset.ContentType);
    }

    /// <summary>
    /// A view is counted from the path alone. The only thing read off the request is whether the
    /// referrer came from somewhere else, which stands in for a new arrival without identifying
    /// anybody (A-27).
    /// </summary>
    private Task CountViewAsync(string path, CancellationToken cancellationToken)
    {
        var referrer = Request.Headers.Referer.ToString();
        var host = Request.Host.Value ?? string.Empty;
        var isNewArrival = string.IsNullOrEmpty(referrer)
            || (host.Length > 0 && !referrer.Contains(host, StringComparison.OrdinalIgnoreCase));

        return _pageViews.RecordViewAsync(path, isNewArrival, cancellationToken);
    }
}

public sealed record PublicCategory(string Name, string Slug, string? Description, string? IconKey, int ProductCount);

public sealed record PublicProductCard(
    string Name,
    string Slug,
    string Tagline,
    string Category,
    string CategorySlug,
    bool IsFeatured,
    decimal? FromPrice,
    string? Currency,
    bool HasFreeTier,
    bool HasLiveDemo);

public sealed record PublicFeature(string Name, string? Description, string? GroupName);

public sealed record PublicScreenshot(string Url, string? Caption, string? AltText);

public sealed record PublicPlanCell(string Feature, string Availability, string? LimitValue);

public sealed record PublicPlan(
    string Name,
    decimal Price,
    string Currency,
    string BillingPeriod,
    int IncludedSeats,
    bool IsFreeTier,
    bool IsRecommended,
    IReadOnlyList<PublicPlanCell> Cells);

public sealed record PublicFaq(string Question, string Answer);

public sealed record PublicDemo(string Url, string? Username, string? Password);

public sealed record PublicProductPage(
    string Name,
    string Slug,
    string Tagline,
    string Summary,
    string? Body,
    string Category,
    string CategorySlug,
    string MetaTitle,
    string? MetaDescription,
    IReadOnlyList<PublicFeature> Features,
    IReadOnlyList<PublicScreenshot> Screenshots,
    IReadOnlyList<PublicPlan> Plans,
    IReadOnlyList<PublicFaq> Faqs,
    PublicDemo? Demo)
{
    public static PublicProductPage From(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var features = product.Features.OrderBy(f => f.SortOrder).ToList();

        // Every cell of the comparison table is filled in, including the ones nobody set: a blank
        // cell reads as "we did not say", and a buyer comparing three plans deserves an answer in
        // each one (REQ-CAT-007, REQ-CAT-013).
        var plans = product.Plans
            .Where(p => p.IsPublished)
            .OrderBy(p => p.SortOrder)
            .Select(plan => new PublicPlan(
                plan.Name,
                plan.Price,
                plan.Currency,
                plan.BillingPeriod.ToString(),
                plan.IncludedSeats,
                plan.IsFreeTier,
                plan.IsRecommended,
                [.. features.Select(feature =>
                {
                    var cell = plan.PlanFeatures.FirstOrDefault(pf => pf.ProductFeatureId == feature.Id);
                    return new PublicPlanCell(
                        feature.Name,
                        (cell?.Availability ?? FeatureAvailability.NotIncluded).ToString(),
                        cell?.LimitValue);
                })]))
            .ToList();

        // A screenshot whose file was deleted is dropped rather than rendered as a broken image
        // (REQ-CAT-004).
        var screenshots = product.Screenshots
            .Where(s => s.MediaAsset is not null)
            .OrderBy(s => s.SortOrder)
            .Select(s => new PublicScreenshot(
                "/api/v1/public/media/" + s.MediaAsset!.StorageKey,
                s.Caption,
                s.MediaAsset.AltText))
            .ToList();

        // The demo button disappears after two consecutive failed checks, not one: a single
        // timeout is usually the network, and hiding a working demo on every blip would cost more
        // enquiries than it saves (BR-INT-04).
        var demo = product.Demo is not null && product.Demo.ShouldShowPublicly
            ? new PublicDemo(product.Demo.Url, product.Demo.DemoUsername, product.Demo.DemoPassword)
            : null;

        return new PublicProductPage(
            product.Name,
            product.Slug,
            product.Tagline,
            product.Summary,
            product.Body,
            product.Category?.Name ?? string.Empty,
            product.Category?.Slug ?? string.Empty,
            product.Seo?.MetaTitle ?? product.Name,
            product.Seo?.MetaDescription ?? product.Tagline,
            [.. features.Select(f => new PublicFeature(f.Name, f.Description, f.GroupName))],
            screenshots,
            plans,
            [.. product.Faqs.Where(f => f.IsPublished).OrderBy(f => f.SortOrder).Select(f => new PublicFaq(f.Question, f.Answer))],
            demo);
    }
}
