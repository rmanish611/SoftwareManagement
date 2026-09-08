using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// The catalogue as a visitor sees it: published products only, grouped by the industry they serve.
///
/// Nothing here reveals a draft. A product that is not published is simply not in the list, and a
/// category with no published products is not offered as a filter, so a visitor is never sent to an
/// empty page (REQ-CAT-002, NFR-AUTHZ-04).
/// </summary>
[ApiController]
[Route("api/v1/public/catalog")]
[AllowAnonymous]
public sealed class PublicCatalogController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet("categories")]
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
    /// The product cards. An unknown category slug returns an empty list rather than an error: a
    /// stale link from an old newsletter should show "nothing here yet", not a failure page.
    /// </summary>
    [HttpGet("products")]
    public async Task<ActionResult<IReadOnlyList<PublicProductCard>>> Products(
        [FromQuery] string? category,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Products
            .AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published || p.Status == ContentStatus.Modified);

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(p => p.Category!.Slug == category);
        }

        var cards = await query
            .OrderByDescending(p => p.IsFeatured).ThenBy(p => p.SortOrder).ThenBy(p => p.Name)
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

        return Ok(cards);
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
