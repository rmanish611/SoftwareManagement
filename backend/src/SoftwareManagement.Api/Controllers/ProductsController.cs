using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Catalog;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// Product authoring: the catalogue an editor maintains and a buyer eventually reads.
///
/// The rules enforced here are the ones that stop a half-finished product page reaching the public
/// and the pricing rules that stop a typing mistake quoting a customer the wrong number.
/// </summary>
[ApiController]
[Route("api/v1/admin/products")]
public sealed class ProductsController(
    AppDbContext dbContext,
    ISlugService slugs,
    IProductPublishingService publishing) : ControllerBase
{
    private const string ProductPathPrefix = "/products/";

    private readonly AppDbContext _dbContext = dbContext;
    private readonly ISlugService _slugs = slugs;
    private readonly IProductPublishingService _publishing = publishing;

    [HttpGet]
    [Authorize(Policy = Permissions.Catalog.ProductRead)]
    public async Task<ActionResult<IReadOnlyList<ProductSummary>>> List(
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // NFR-PERF-04: a caller asking for more gets the ceiling rather than an error.
        var take = Math.Clamp(pageSize, 1, 100);

        var products = await _dbContext.Products
            .AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Take(take)
            .Select(p => new ProductSummary(
                p.Id,
                p.Name,
                p.Slug,
                p.Category!.Name,
                p.Status.ToString(),
                p.Features.Count,
                p.Screenshots.Count,
                p.Plans.Count))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(products);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Catalog.ProductRead)]
    public async Task<ActionResult<ProductDetail>> Get(Guid id, CancellationToken cancellationToken)
    {
        var product = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        return product is null ? NotFound() : Ok(ProductDetail.From(product));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.ProductWrite)]
    public async Task<ActionResult<ProductDetail>> Create(CreateProductBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var category = await _dbContext.ProductCategories
            .FirstOrDefaultAsync(c => c.Slug == body.CategorySlug, cancellationToken).ConfigureAwait(false);

        if (category is null)
        {
            return CatalogProblem(
                StatusCodes.Status422UnprocessableEntity,
                "UNKNOWN_CATEGORY",
                "Unknown category",
                $"There is no product category with the address \"{body.CategorySlug}\".");
        }

        // A slug the caller typed is validated as typed. Silently shortening it would change the
        // address they asked for without telling them; a slug derived from the name may be
        // shortened, because nobody chose its exact length.
        var explicitSlug = !string.IsNullOrWhiteSpace(body.Slug);

        if (explicitSlug && body.Slug!.Length > Slug.MaxLength)
        {
            return CatalogProblem(
                StatusCodes.Status422UnprocessableEntity,
                "INVALID_SLUG",
                "Slug is too long",
                $"A slug is at most {Slug.MaxLength} characters.");
        }

        var slug = Slug.From(explicitSlug ? body.Slug : body.Name);

        if (!Slug.IsValid(slug))
        {
            return CatalogProblem(
                StatusCodes.Status422UnprocessableEntity,
                "INVALID_SLUG",
                "Slug is not valid",
                $"A slug is {Slug.MinLength} to {Slug.MaxLength} characters, lowercase letters, digits and single hyphens.");
        }

        if (!await IsSlugFreeAsync(slug, null, cancellationToken).ConfigureAwait(false))
        {
            var suggestion = await SuggestSlugAsync(slug, cancellationToken).ConfigureAwait(false);
            return CatalogProblem(
                StatusCodes.Status409Conflict,
                "SLUG_TAKEN",
                "Slug is taken",
                $"That address is already in use. Try \"{suggestion}\".",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["suggestedSlug"] = suggestion });
        }

        var actor = ActorEmail();

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = body.Name.Trim(),
            Slug = slug,
            Tagline = (body.Tagline ?? string.Empty).Trim(),
            Summary = (body.Summary ?? string.Empty).Trim(),
            Body = body.Body,
            CategoryId = category.Id,
            SortOrder = body.SortOrder,
            Status = ContentStatus.Draft,
            CreatedBy = actor,
            Seo = new SeoMetadata
            {
                Id = Guid.NewGuid(),
                MetaTitle = body.Name.Trim(),
                MetaDescription = (body.Tagline ?? string.Empty).Trim(),
                CreatedBy = actor,
            },
        };

        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var saved = await LoadAsync(product.Id, cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, ProductDetail.From(saved!));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Catalog.ProductWrite)]
    public async Task<ActionResult<ProductDetail>> Update(Guid id, UpdateProductBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var product = await _dbContext.Products
            .Include(p => p.Seo)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

        if (product is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(body.CategorySlug))
        {
            var category = await _dbContext.ProductCategories
                .FirstOrDefaultAsync(c => c.Slug == body.CategorySlug, cancellationToken).ConfigureAwait(false);

            if (category is null)
            {
                return CatalogProblem(
                    StatusCodes.Status422UnprocessableEntity,
                    "UNKNOWN_CATEGORY",
                    "Unknown category",
                    $"There is no product category with the address \"{body.CategorySlug}\".");
            }

            product.CategoryId = category.Id;
        }

        var previousSlug = product.Slug;
        var wasPublished = product.Status is ContentStatus.Published or ContentStatus.Modified;

        if (!string.IsNullOrWhiteSpace(body.Slug) && !string.Equals(body.Slug, product.Slug, StringComparison.Ordinal))
        {
            if (body.Slug!.Length > Slug.MaxLength || !Slug.IsValid(body.Slug))
            {
                return CatalogProblem(
                    StatusCodes.Status422UnprocessableEntity,
                    "INVALID_SLUG",
                    "Slug is not valid",
                    $"A slug is {Slug.MinLength} to {Slug.MaxLength} characters, lowercase letters, digits and single hyphens.");
            }

            if (!await IsSlugFreeAsync(body.Slug, id, cancellationToken).ConfigureAwait(false))
            {
                var suggestion = await SuggestSlugAsync(body.Slug, cancellationToken).ConfigureAwait(false);
                return CatalogProblem(
                    StatusCodes.Status409Conflict,
                    "SLUG_TAKEN",
                    "Slug is taken",
                    $"That address is already in use. Try \"{suggestion}\".",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["suggestedSlug"] = suggestion });
            }

            product.Slug = body.Slug;
        }

        product.Name = string.IsNullOrWhiteSpace(body.Name) ? product.Name : body.Name.Trim();
        product.Tagline = body.Tagline ?? product.Tagline;
        product.Summary = body.Summary ?? product.Summary;
        product.Body = body.Body ?? product.Body;
        product.DocsUrl = body.DocsUrl ?? product.DocsUrl;
        product.RepositoryUrl = body.RepositoryUrl ?? product.RepositoryUrl;
        product.IsFeatured = body.IsFeatured ?? product.IsFeatured;
        product.SortOrder = body.SortOrder ?? product.SortOrder;
        product.ModifiedBy = ActorEmail();

        // Editing published content moves it to Modified: the public keeps seeing the last
        // published version until the editor publishes again (BR-SITE-03, applied to products).
        if (product.Status == ContentStatus.Published)
        {
            product.Status = ContentStatus.Modified;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // A renamed product that was live leaves its old address behind, redirected (BR-SITE-06).
        if (wasPublished && !string.Equals(previousSlug, product.Slug, StringComparison.Ordinal))
        {
            await _slugs.RecordRenameAsync(
                ProductPathPrefix + previousSlug,
                ProductPathPrefix + product.Slug,
                cancellationToken).ConfigureAwait(false);
        }

        var saved = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        return Ok(ProductDetail.From(saved!));
    }

    [HttpPost("{id:guid}/features")]
    [Authorize(Policy = Permissions.Catalog.ProductWrite)]
    public async Task<ActionResult<FeatureDetail>> AddFeature(Guid id, FeatureBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!await _dbContext.Products.AnyAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return NotFound();
        }

        var name = (body.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return CatalogProblem(
                StatusCodes.Status422UnprocessableEntity,
                "FEATURE_NAME_REQUIRED",
                "The feature needs a name",
                "A feature row with no name says nothing on the product page.");
        }

        // Two features with one name is a copy-and-paste mistake, and it makes the plan comparison
        // table ambiguous (REQ-CAT-003).
        if (await _dbContext.ProductFeatures.AnyAsync(f => f.ProductId == id && f.Name == name, cancellationToken).ConfigureAwait(false))
        {
            return CatalogProblem(
                StatusCodes.Status409Conflict,
                "DUPLICATE_FEATURE",
                "That feature is already listed",
                $"\"{name}\" is already a feature of this product.");
        }

        var used = await _dbContext.ProductFeatures
            .Where(f => f.ProductId == id)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        var feature = new ProductFeature
        {
            Id = Guid.NewGuid(),
            ProductId = id,
            Name = name,
            Description = body.Description,
            GroupName = body.GroupName,
            SortOrder = used + 1,
            IsHighlighted = body.IsHighlighted,
            CreatedBy = ActorEmail(),
        };

        _dbContext.ProductFeatures.Add(feature);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new FeatureDetail(feature.Id, feature.Name, feature.Description, feature.GroupName, feature.SortOrder, feature.IsHighlighted));
    }

    /// <summary>
    /// Reorders a product's features. The caller sends the ids in the order it wants them; the
    /// stored positions are rewritten as 1..n, so a reorder can never leave a gap or two features
    /// sharing a position (REQ-CAT-003).
    /// </summary>
    [HttpPut("{id:guid}/features/order")]
    [Authorize(Policy = Permissions.Catalog.ProductWrite)]
    public async Task<ActionResult<IReadOnlyList<FeatureDetail>>> ReorderFeatures(Guid id, FeatureOrderBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var features = await _dbContext.ProductFeatures
            .Where(f => f.ProductId == id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (features.Count == 0)
        {
            return NotFound();
        }

        var requested = body.OrderedIds ?? [];

        if (requested.Count != features.Count
            || requested.Distinct().Count() != requested.Count
            || requested.Exists(requestedId => !features.Exists(f => f.Id == requestedId)))
        {
            return CatalogProblem(
                StatusCodes.Status422UnprocessableEntity,
                "INCOMPLETE_ORDER",
                "The new order is incomplete",
                $"Send each of this product's {features.Count} feature ids exactly once. A partial list would leave the rest in an undefined position.");
        }

        var actor = ActorEmail();
        for (var position = 0; position < requested.Count; position++)
        {
            var feature = features.Find(f => f.Id == requested[position])!;
            feature.SortOrder = position + 1;
            feature.ModifiedBy = actor;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(features
            .OrderBy(f => f.SortOrder)
            .Select(f => new FeatureDetail(f.Id, f.Name, f.Description, f.GroupName, f.SortOrder, f.IsHighlighted))
            .ToList());
    }

    [HttpPost("{id:guid}/screenshots")]
    [Authorize(Policy = Permissions.Catalog.ProductWrite)]
    public async Task<ActionResult<ScreenshotDetail>> AddScreenshot(Guid id, ScreenshotBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!await _dbContext.Products.AnyAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return NotFound();
        }

        if (!await _dbContext.MediaAssets.AnyAsync(m => m.Id == body.MediaAssetId, cancellationToken).ConfigureAwait(false))
        {
            return CatalogProblem(
                StatusCodes.Status422UnprocessableEntity,
                "UNKNOWN_MEDIA",
                "That image is not in the library",
                "Upload the image to the media library first, then attach it here.");
        }

        var used = await _dbContext.ProductScreenshots
            .Where(s => s.ProductId == id)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        var screenshot = new ProductScreenshot
        {
            Id = Guid.NewGuid(),
            ProductId = id,
            MediaAssetId = body.MediaAssetId,
            Caption = body.Caption,
            SortOrder = used + 1,
            CreatedBy = ActorEmail(),
        };

        _dbContext.ProductScreenshots.Add(screenshot);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new ScreenshotDetail(screenshot.Id, screenshot.MediaAssetId, screenshot.Caption, screenshot.SortOrder));
    }

    [HttpPost("{id:guid}/plans")]
    [Authorize(Policy = Permissions.Catalog.PlanWrite)]
    public async Task<ActionResult<PlanDetail>> AddPlan(Guid id, PlanBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var product = await _dbContext.Products
            .Include(p => p.Plans)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

        if (product is null)
        {
            return NotFound();
        }

        var billing = ParseBilling(body.BillingPeriod);

        var rejection = ValidatePrice(product, body.Price, body.IsFreeTier, billing, null);
        if (rejection is not null)
        {
            return rejection;
        }

        var plan = new PricingPlan
        {
            Id = Guid.NewGuid(),
            ProductId = id,
            Name = (body.Name ?? string.Empty).Trim(),
            Price = body.Price,
            Currency = NormaliseCurrency(body.Currency),
            BillingPeriod = billing,
            IncludedSeats = Math.Max(1, body.IncludedSeats),
            IsFreeTier = body.IsFreeTier,
            SetupFee = body.SetupFee,
            SortOrder = product.Plans.Count == 0 ? 1 : product.Plans.Max(p => p.SortOrder) + 1,
            IsPublished = body.IsPublished,
            CreatedBy = ActorEmail(),
        };

        // The connection retries transient faults, so a transaction this code opens itself has to
        // run inside the retry strategy: otherwise a retry would resume half a transaction that the
        // failure already rolled back.
        await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            if (body.IsRecommended)
            {
                await ClearRecommendedAsync(id, null, cancellationToken).ConfigureAwait(false);
                plan.IsRecommended = true;
            }

            _dbContext.PricingPlans.Add(plan);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return Ok(PlanDetail.From(plan));
    }

    [HttpPut("{id:guid}/plans/{planId:guid}")]
    [Authorize(Policy = Permissions.Catalog.PlanWrite)]
    public async Task<ActionResult<PlanDetail>> UpdatePlan(Guid id, Guid planId, PlanBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var product = await _dbContext.Products
            .Include(p => p.Plans)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

        var plan = product?.Plans.FirstOrDefault(p => p.Id == planId);
        if (product is null || plan is null)
        {
            return NotFound();
        }

        var billing = ParseBilling(body.BillingPeriod);

        var rejection = ValidatePrice(product, body.Price, body.IsFreeTier, billing, planId);
        if (rejection is not null)
        {
            return rejection;
        }

        await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            // The previous holder is cleared first, in its own statement inside this transaction,
            // so the database's one-recommended-plan index is satisfied at every step: two plans
            // never carry the flag at once, not even briefly, and a failure part-way rolls both
            // back (BR-CAT-03).
            if (body.IsRecommended && !plan.IsRecommended)
            {
                await ClearRecommendedAsync(id, planId, cancellationToken).ConfigureAwait(false);
            }

            plan.Name = string.IsNullOrWhiteSpace(body.Name) ? plan.Name : body.Name.Trim();
            plan.Price = body.Price;
            plan.Currency = NormaliseCurrency(body.Currency);
            plan.BillingPeriod = billing;
            plan.IncludedSeats = Math.Max(1, body.IncludedSeats);
            plan.IsFreeTier = body.IsFreeTier;
            plan.IsRecommended = body.IsRecommended;
            plan.SetupFee = body.SetupFee;
            plan.IsPublished = body.IsPublished;
            plan.ModifiedBy = ActorEmail();

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return Ok(PlanDetail.From(plan));
    }

    [HttpPut("{id:guid}/plans/{planId:guid}/features")]
    [Authorize(Policy = Permissions.Catalog.PlanWrite)]
    public async Task<IActionResult> SetPlanFeature(Guid id, Guid planId, PlanFeatureBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var planExists = await _dbContext.PricingPlans
            .AnyAsync(p => p.Id == planId && p.ProductId == id, cancellationToken).ConfigureAwait(false);

        var featureExists = await _dbContext.ProductFeatures
            .AnyAsync(f => f.Id == body.ProductFeatureId && f.ProductId == id, cancellationToken).ConfigureAwait(false);

        if (!planExists || !featureExists)
        {
            return NotFound();
        }

        var availability = Enum.TryParse<FeatureAvailability>(body.Availability, ignoreCase: true, out var parsed)
            ? parsed
            : FeatureAvailability.Included;

        // "Limited" with no number tells a buyer nothing at all (REQ-CAT-007).
        if (availability == FeatureAvailability.Limited && string.IsNullOrWhiteSpace(body.LimitValue))
        {
            return CatalogProblem(
                StatusCodes.Status422UnprocessableEntity,
                "LIMIT_REQUIRED",
                "The limit is missing",
                "A limited feature needs the limit itself, for example \"5 users\" or \"10 GB\".");
        }

        var existing = await _dbContext.PlanFeatures
            .FirstOrDefaultAsync(f => f.PricingPlanId == planId && f.ProductFeatureId == body.ProductFeatureId, cancellationToken)
            .ConfigureAwait(false);

        var limit = availability == FeatureAvailability.Limited ? body.LimitValue : null;

        if (existing is null)
        {
            _dbContext.PlanFeatures.Add(new PlanFeature
            {
                Id = Guid.NewGuid(),
                PricingPlanId = planId,
                ProductFeatureId = body.ProductFeatureId,
                Availability = availability,
                LimitValue = limit,
                CreatedBy = ActorEmail(),
            });
        }
        else
        {
            existing.Availability = availability;
            existing.LimitValue = limit;
            existing.ModifiedBy = ActorEmail();
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPut("{id:guid}/demo")]
    [Authorize(Policy = Permissions.Catalog.ProductWrite)]
    public async Task<ActionResult<DemoDetail>> SetDemo(Guid id, DemoBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var product = await _dbContext.Products
            .Include(p => p.Demo)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

        if (product is null)
        {
            return NotFound();
        }

        if (!Uri.TryCreate(body.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return CatalogProblem(
                StatusCodes.Status422UnprocessableEntity,
                "INVALID_DEMO_URL",
                "The demo address is not a URL",
                "Give the full address of the demo, starting with https://.");
        }

        if (product.Demo is null)
        {
            product.Demo = new DemoEnvironment
            {
                Id = Guid.NewGuid(),
                ProductId = id,
                CreatedBy = ActorEmail(),
            };

            _dbContext.DemoEnvironments.Add(product.Demo);
        }
        else
        {
            product.Demo.ModifiedBy = ActorEmail();
        }

        product.Demo.Url = body.Url;
        product.Demo.DemoUsername = body.DemoUsername;
        product.Demo.DemoPassword = body.DemoPassword;
        product.Demo.IsEnabled = body.IsEnabled;

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The credentials are deliberately absent from this response and from every log line. Only
        // the reader below returns them, and only to a caller holding the demo-credentials
        // permission (BR-CAT-06, NFR-PRIV-03).
        return Ok(new DemoDetail(product.Demo.Url, product.Demo.IsEnabled, product.Demo.HealthState.ToString(), product.Demo.DemoUsername is not null));
    }

    /// <summary>
    /// The demo sign-in that appears on the published product page. It is a separate endpoint
    /// behind its own permission, so the credentials are never carried along by an ordinary read of
    /// the product.
    /// </summary>
    [HttpGet("{id:guid}/demo/credentials")]
    [Authorize(Policy = Permissions.Catalog.DemoCredentials)]
    public async Task<ActionResult<DemoCredentials>> GetDemoCredentials(Guid id, CancellationToken cancellationToken)
    {
        var demo = await _dbContext.DemoEnvironments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ProductId == id, cancellationToken).ConfigureAwait(false);

        return demo is null ? NotFound() : Ok(new DemoCredentials(demo.Url, demo.DemoUsername, demo.DemoPassword));
    }

    /// <summary>
    /// What is still missing before this product could be published. The admin screen asks before
    /// showing the button, so an editor is never told "no" only after pressing it.
    /// </summary>
    [HttpGet("{id:guid}/readiness")]
    [Authorize(Policy = Permissions.Catalog.ProductRead)]
    public async Task<ActionResult<ReadinessReport>> Readiness(Guid id, CancellationToken cancellationToken)
    {
        var readiness = await _publishing.ReadinessAsync(id, cancellationToken).ConfigureAwait(false);

        return readiness is null
            ? NotFound()
            : Ok(new ReadinessReport(
                readiness.IsReady,
                readiness.Explain(),
                [.. readiness.Shortfalls.Select(s => new ShortfallReport(s.What, s.Has, s.Needs))]));
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = Permissions.Catalog.ProductPublish)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
    {
        var result = await _publishing.PublishAsync(id, ActorEmail(), cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            ProductPublishOutcome.Published => NoContent(),
            ProductPublishOutcome.NotFound => NotFound(),
            ProductPublishOutcome.AlreadyArchived => CatalogProblem(
                StatusCodes.Status409Conflict,
                "PRODUCT_ARCHIVED",
                "This product is archived",
                "Archived products stay out of the catalogue. Create a new product rather than reviving this one."),

            // The failing counts go in the response, not just "not ready": an editor should not have
            // to open three screens to find out which threshold they are short of (REQ-CAT-009).
            _ => CatalogProblem(
                StatusCodes.Status422UnprocessableEntity,
                "NOT_READY_TO_PUBLISH",
                "Not enough to publish yet",
                result.Readiness?.Explain() ?? "This product is not ready to publish.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["shortfalls"] = result.Readiness?.Shortfalls
                        .Select(s => new ShortfallReport(s.What, s.Has, s.Needs))
                        .ToList(),
                }),
        };
    }

    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Policy = Permissions.Catalog.ProductPublish)]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken cancellationToken)
    {
        var result = await _publishing.UnpublishAsync(id, ActorEmail(), cancellationToken).ConfigureAwait(false);
        return result.Outcome == ProductPublishOutcome.NotFound ? NotFound() : NoContent();
    }

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = Permissions.Catalog.ProductArchive)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var result = await _publishing.ArchiveAsync(id, ActorEmail(), cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            ProductPublishOutcome.Archived => NoContent(),
            ProductPublishOutcome.NotFound => NotFound(),
            _ => CatalogProblem(
                StatusCodes.Status409Conflict,
                "PRODUCT_ARCHIVED",
                "This product is already archived",
                "Nothing to do: it is already out of the catalogue."),
        };
    }

    /// <summary>
    /// Deleting a product is refused once it has been public, because its address, its price and
    /// its name are cited outside this system by then. Archiving is the operation that retires a
    /// product; deleting is only for something created by mistake and never shown to anyone
    /// (BR-CAT-08, REQ-CAT-015).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Catalog.ProductArchive)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

        if (product is null)
        {
            return NotFound();
        }

        var wasEverPublic = product.PublishedAtUtc is not null || product.Status != ContentStatus.Draft;

        if (wasEverPublic)
        {
            return CatalogProblem(
                StatusCodes.Status409Conflict,
                "PRODUCT_IN_USE",
                "This product cannot be deleted",
                "It has been published, so its address and its price are quoted outside this system. Archive it instead: it leaves the catalogue and the history stays.");
        }

        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("{id:guid}/faqs")]
    [Authorize(Policy = Permissions.Catalog.ProductWrite)]
    public async Task<ActionResult<FaqDetail>> AddFaq(Guid id, FaqBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!await _dbContext.Products.AnyAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return NotFound();
        }

        var question = (body.Question ?? string.Empty).Trim();
        var answer = (body.Answer ?? string.Empty).Trim();

        if (question.Length == 0 || answer.Length == 0)
        {
            return CatalogProblem(
                StatusCodes.Status422UnprocessableEntity,
                "FAQ_INCOMPLETE",
                "A question needs an answer",
                "Half of a question and answer pair is worse on the page than neither half.");
        }

        var used = await _dbContext.FaqItems
            .Where(f => f.ProductId == id)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        var faq = new FaqItem
        {
            Id = Guid.NewGuid(),
            ProductId = id,
            Question = question,
            Answer = answer,
            SortOrder = used + 1,
            IsPublished = body.IsPublished,
            CreatedBy = ActorEmail(),
        };

        _dbContext.FaqItems.Add(faq);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new FaqDetail(faq.Id, faq.Question, faq.Answer, faq.SortOrder, faq.IsPublished));
    }

    private static string NormaliseCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "INR" : currency.Trim().ToUpperInvariant();

    private static BillingPeriod ParseBilling(string? value) =>
        Enum.TryParse<BillingPeriod>(value, ignoreCase: true, out var parsed) ? parsed : BillingPeriod.Monthly;

    /// <summary>
    /// The price rules from BR-CAT-02 and BR-CAT-04, in one place so the create and edit paths
    /// cannot drift apart.
    /// </summary>
    private ObjectResult? ValidatePrice(Product product, decimal price, bool isFreeTier, BillingPeriod billing, Guid? excludePlanId)
    {
        if (price < 0)
        {
            return CatalogProblem(
                StatusCodes.Status422UnprocessableEntity,
                "INVALID_PRICE",
                "A price cannot be negative",
                "Enter zero and tick the free-tier box for a free plan, or enter the real amount.");
        }

        if (price == 0 && !isFreeTier)
        {
            return CatalogProblem(
                StatusCodes.Status422UnprocessableEntity,
                "INVALID_PRICE",
                "A plan priced at zero must say it is free",
                "Tick the free-tier box, so an unfinished price is never published as though the plan cost nothing.");
        }

        if (billing != BillingPeriod.Yearly)
        {
            return null;
        }

        // A yearly price above twelve monthly payments charges a customer more for paying up front,
        // which is almost always a typing mistake (BR-CAT-04).
        var monthly = product.Plans
            .Where(p => p.BillingPeriod == BillingPeriod.Monthly && p.Price > 0 && p.Id != excludePlanId)
            .Select(p => p.Price)
            .DefaultIfEmpty(0m)
            .Min();

        if (PricingPlan.YearlyPriceIsPlausible(price, monthly))
        {
            return null;
        }

        return CatalogProblem(
            StatusCodes.Status422UnprocessableEntity,
            "IMPLAUSIBLE_PRICE",
            "The yearly price looks wrong",
            FormattableString.Invariant($"A year at {price} costs more than twelve monthly payments of {monthly}. Check the figure before saving."));
    }

    /// <summary>Clears the flag wherever it sits and reports how many rows it moved.</summary>
    private Task<int> ClearRecommendedAsync(Guid productId, Guid? exceptPlanId, CancellationToken cancellationToken) =>
        _dbContext.PricingPlans
            .Where(p => p.ProductId == productId && p.IsRecommended && (exceptPlanId == null || p.Id != exceptPlanId))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsRecommended, false), cancellationToken);

    private Task<Product?> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Products
            .Include(p => p.Category)
            .Include(p => p.Features)
            .Include(p => p.Screenshots)
            .Include(p => p.Plans)
            .Include(p => p.Demo)
            .Include(p => p.Seo)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    /// <summary>
    /// A product slug is free only when no product holds it and no redirect already claims the
    /// address. A slug that was ever live sits in someone's bookmarks and in search results, so it
    /// is never handed to a different product (BR-CAT-07).
    /// </summary>
    private async Task<bool> IsSlugFreeAsync(string slug, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (await _dbContext.Products.AnyAsync(p => p.Slug == slug && (exceptId == null || p.Id != exceptId), cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var path = ProductPathPrefix + slug;
        return !await _dbContext.Redirects.AnyAsync(r => r.FromPath == path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> SuggestSlugAsync(string desired, CancellationToken cancellationToken)
    {
        for (var suffix = 2; suffix < 100; suffix++)
        {
            var tail = "-" + suffix.ToString(CultureInfo.InvariantCulture);

            var candidate = desired.Length + tail.Length > Slug.MaxLength
                ? desired[..(Slug.MaxLength - tail.Length)].TrimEnd('-') + tail
                : desired + tail;

            if (await IsSlugFreeAsync(candidate, null, cancellationToken).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        return $"{desired}-{Guid.NewGuid():N}"[..Slug.MaxLength];
    }

    /// <summary>
    /// One problem-details shape for the whole catalogue, carrying a stable machine-readable code
    /// beside the sentence a person reads. The client switches on the code, so the wording can
    /// change without breaking anything.
    /// </summary>
    private ObjectResult CatalogProblem(int status, string code, string title, string detail, Dictionary<string, object?>? extra = null)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code };

        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                extensions[key] = value;
            }
        }

        return Problem(
            title: title,
            detail: detail,
            statusCode: status,
            type: "https://softwaremanagement.example/errors/" + code.ToLowerInvariant().Replace('_', '-'),
            extensions: extensions);
    }

    private string ActorEmail() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

public sealed record CreateProductBody(
    string Name,
    string? Slug,
    string? Tagline,
    string? Summary,
    string? Body,
    string CategorySlug,
    int SortOrder = 0);

public sealed record UpdateProductBody(
    string? Name,
    string? Slug,
    string? Tagline,
    string? Summary,
    string? Body,
    string? CategorySlug,
    string? DocsUrl,
    string? RepositoryUrl,
    bool? IsFeatured,
    int? SortOrder);

public sealed record FeatureBody(string Name, string? Description, string? GroupName, bool IsHighlighted);

public sealed record FeatureOrderBody(List<Guid> OrderedIds);

public sealed record ScreenshotBody(Guid MediaAssetId, string? Caption);

public sealed record PlanBody(
    string Name,
    decimal Price,
    string? Currency,
    string BillingPeriod,
    int IncludedSeats,
    bool IsFreeTier,
    bool IsRecommended,
    decimal SetupFee,
    bool IsPublished);

public sealed record PlanFeatureBody(Guid ProductFeatureId, string Availability, string? LimitValue);

public sealed record DemoBody(string Url, string? DemoUsername, string? DemoPassword, bool IsEnabled);

public sealed record ProductSummary(
    Guid Id,
    string Name,
    string Slug,
    string Category,
    string Status,
    int FeatureCount,
    int ScreenshotCount,
    int PlanCount);

public sealed record FeatureDetail(Guid Id, string Name, string? Description, string? GroupName, int SortOrder, bool IsHighlighted);

public sealed record ScreenshotDetail(Guid Id, Guid MediaAssetId, string? Caption, int SortOrder);

public sealed record PlanDetail(
    Guid Id,
    string Name,
    decimal Price,
    string Currency,
    string BillingPeriod,
    int IncludedSeats,
    bool IsFreeTier,
    bool IsRecommended,
    bool IsPublished,
    int SortOrder)
{
    public static PlanDetail From(PricingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new PlanDetail(
            plan.Id,
            plan.Name,
            plan.Price,
            plan.Currency,
            plan.BillingPeriod.ToString(),
            plan.IncludedSeats,
            plan.IsFreeTier,
            plan.IsRecommended,
            plan.IsPublished,
            plan.SortOrder);
    }
}

public sealed record DemoDetail(string Url, bool IsEnabled, string HealthState, bool HasCredentials);

public sealed record DemoCredentials(string Url, string? Username, string? Password);

public sealed record ProductDetail(
    Guid Id,
    string Name,
    string Slug,
    string Tagline,
    string Summary,
    string? Body,
    string Category,
    string CategorySlug,
    string Status,
    bool IsFeatured,
    IReadOnlyList<FeatureDetail> Features,
    IReadOnlyList<ScreenshotDetail> Screenshots,
    IReadOnlyList<PlanDetail> Plans,
    DemoDetail? Demo)
{
    public static ProductDetail From(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new ProductDetail(
            product.Id,
            product.Name,
            product.Slug,
            product.Tagline,
            product.Summary,
            product.Body,
            product.Category?.Name ?? string.Empty,
            product.Category?.Slug ?? string.Empty,
            product.Status.ToString(),
            product.IsFeatured,
            [.. product.Features.OrderBy(f => f.SortOrder).Select(f => new FeatureDetail(f.Id, f.Name, f.Description, f.GroupName, f.SortOrder, f.IsHighlighted))],
            [.. product.Screenshots.OrderBy(s => s.SortOrder).Select(s => new ScreenshotDetail(s.Id, s.MediaAssetId, s.Caption, s.SortOrder))],
            [.. product.Plans.OrderBy(p => p.SortOrder).Select(PlanDetail.From)],
            product.Demo is null ? null : new DemoDetail(product.Demo.Url, product.Demo.IsEnabled, product.Demo.HealthState.ToString(), product.Demo.DemoUsername is not null));
    }
}

public sealed record FaqBody(string Question, string Answer, bool IsPublished);

public sealed record FaqDetail(Guid Id, string Question, string Answer, int SortOrder, bool IsPublished);

public sealed record ShortfallReport(string What, int Has, int Needs);

public sealed record ReadinessReport(bool IsReady, string Explanation, IReadOnlyList<ShortfallReport> Shortfalls);
