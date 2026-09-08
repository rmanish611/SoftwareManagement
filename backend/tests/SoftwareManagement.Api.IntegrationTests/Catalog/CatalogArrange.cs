using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Api.IntegrationTests.Content;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Catalog;

/// <summary>
/// Arrangement helpers for the catalogue tests. They exist so each test reads as the one thing it
/// asserts rather than as ten lines of setup, and so every test starts from a product nobody else
/// touches: names carry a fresh nonce, because these tests share one database.
/// </summary>
public static class CatalogArrange
{
    public static string Nonce() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Fails with the server's own explanation rather than a bare status code. An arrangement that
    /// breaks should say why on the first run, not on the second one with extra logging added.
    /// </summary>
    public static async Task EnsureCreatedAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {body}");
    }

    public static async Task<ProductRow> CreateProductAsync(
        HttpClient client,
        string name,
        string? slug = null,
        string categorySlug = "erp")
    {
        var response = await client.PostAsJsonAsync("/api/v1/admin/products", new
        {
            name,
            slug,
            tagline = "Runs the whole business from one place.",
            summary = "A summary long enough to read like a real product rather than a placeholder.",
            body = "<p>Body</p>",
            categorySlug,
        });

        await EnsureCreatedAsync(response);
        return (await response.Content.ReadFromJsonAsync<ProductRow>())!;
    }

    public static async Task<FeatureRow> AddFeatureAsync(HttpClient client, Guid productId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/admin/products/{productId}/features", new
        {
            name,
            description = "What this capability does for the customer.",
            groupName = "Core",
            isHighlighted = false,
        });

        await EnsureCreatedAsync(response);
        return (await response.Content.ReadFromJsonAsync<FeatureRow>())!;
    }

    public static async Task<PlanRow> AddPlanAsync(
        HttpClient client,
        Guid productId,
        string name,
        decimal price,
        string billingPeriod = "Monthly",
        bool isRecommended = false,
        bool isPublished = true,
        bool isFreeTier = false)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/admin/products/{productId}/plans", new
        {
            name,
            price,
            currency = "INR",
            billingPeriod,
            includedSeats = 5,
            isFreeTier,
            isRecommended,
            setupFee = 0m,
            isPublished,
        });

        await EnsureCreatedAsync(response);
        return (await response.Content.ReadFromJsonAsync<PlanRow>())!;
    }

    /// <summary>
    /// Puts a media asset in the library without going through an upload. The upload path has its
    /// own tests; here the row is only the thing a screenshot points at.
    /// </summary>
    public static async Task<Guid> AddMediaAssetAsync(ContentFixture fixture, string nonce)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            FileName = $"screenshot-{nonce}.png",
            StorageKey = $"catalog/{nonce}.png",
            ContentType = "image/png",
            SizeBytes = 2048,
            Width = 1280,
            Height = 720,
            AltText = "The dashboard",
            Sha256 = nonce.PadRight(64, '0'),
            Kind = MediaKind.Image,
            CreatedBy = "test-fixture",
        };

        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    /// <summary>
    /// Publishes a product directly. The publish endpoint with its readiness rules arrives with the
    /// public catalogue in P06; these tests only need a product that is live so the public reader
    /// has something to return.
    /// </summary>
    public static async Task PublishAsync(ContentFixture fixture, Guid productId)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = await db.Products.FirstAsync(p => p.Id == productId);
        product.Status = ContentStatus.Published;
        product.PublishedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public static async Task<int> CountRecommendedAsync(ContentFixture fixture, Guid productId)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PricingPlans.CountAsync(p => p.ProductId == productId && p.IsRecommended);
    }

    public static async Task<ProductCategory> CreateCategoryAsync(ContentFixture fixture, string slug, string name)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = new ProductCategory
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Name = name,
            SortOrder = 90,
            IsPublished = true,
            CreatedBy = "test-fixture",
        };

        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    /// <summary>
    /// A product that clears every publishing threshold: three features, a screenshot and a
    /// published plan, in a published category. Most of the public-catalogue tests start from one,
    /// and building it by hand in each of them would bury the thing each test is actually about.
    /// </summary>
    public static async Task<ProductRow> BuildPublishableProductAsync(
        ContentFixture fixture,
        HttpClient client,
        string name,
        string nonce,
        string categorySlug = "erp")
    {
        var product = await CreateProductAsync(client, name, categorySlug: categorySlug);
        await BuildPublishableProductAsync(fixture, client, product);
        return product;
    }

    /// <summary>Brings an existing draft up to the publishing thresholds.</summary>
    public static async Task BuildPublishableProductAsync(ContentFixture fixture, HttpClient client, ProductRow product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var nonce = Nonce();

        await AddFeatureAsync(client, product.Id, $"Feature one {nonce}");
        await AddFeatureAsync(client, product.Id, $"Feature two {nonce}");
        await AddFeatureAsync(client, product.Id, $"Feature three {nonce}");

        var asset = await AddMediaAssetAsync(fixture, nonce);
        var screenshot = await client.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/screenshots",
            new { mediaAssetId = asset, caption = "The dashboard" });
        await EnsureCreatedAsync(screenshot);

        await AddPlanAsync(client, product.Id, "Starter", 1999m);
    }

    /// <summary>
    /// Marks a demo as having failed its checks. The hourly checker itself arrives in P13; the page
    /// only needs to know what the checker would have recorded.
    /// </summary>
    public static async Task MarkDemoDownAsync(ContentFixture fixture, Guid productId)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var demo = await db.DemoEnvironments.FirstAsync(d => d.ProductId == productId);
        demo.HealthState = DemoHealth.Down;
        demo.ConsecutiveFailures = DemoEnvironment.FailuresBeforeDown;
        demo.LastCheckedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public sealed record ProductRow(Guid Id, string Name, string Slug, string Category, string CategorySlug, string Status);

    public sealed record ProductDetailRow(Guid Id, string Name, string Slug, IReadOnlyList<FeatureRow> Features, IReadOnlyList<PlanRow> Plans);

    public sealed record PublicFeatureRow(string Name, string? Description, string? GroupName);

    public sealed record PublicScreenshotRow(string Url, string? Caption, string? AltText);

    public sealed record PublicCellRow(string Feature, string Availability, string? LimitValue);

    public sealed record PublicPlanRow(
        string Name,
        decimal Price,
        string Currency,
        string BillingPeriod,
        bool IsRecommended,
        IReadOnlyList<PublicCellRow> Cells);

    public sealed record PublicFaqRow(string Question, string Answer);

    public sealed record PublicDemoRow(string Url, string? Username, string? Password);

    public sealed record PublicPage(
        string Name,
        string Slug,
        string Tagline,
        string Summary,
        string MetaTitle,
        string? MetaDescription,
        IReadOnlyList<PublicFeatureRow> Features,
        IReadOnlyList<PublicScreenshotRow> Screenshots,
        IReadOnlyList<PublicPlanRow> Plans,
        IReadOnlyList<PublicFaqRow> Faqs,
        PublicDemoRow? Demo);

    public sealed record ShortfallRow(string What, int Has, int Needs);

    public sealed record ReadinessRow(bool IsReady, string Explanation, IReadOnlyList<ShortfallRow> Shortfalls);

    public sealed record FeatureRow(Guid Id, string Name, int SortOrder);

    public sealed record PlanRow(Guid Id, string Name, decimal Price, string Currency, string BillingPeriod, bool IsRecommended, bool IsPublished);

    public sealed record CategoryRow(Guid Id, string Name, string Slug, int ProductCount);

    public sealed record ScreenshotRow(Guid Id, Guid MediaAssetId, string? Caption, int SortOrder);

    public sealed record PublicCard(string Name, string Slug, string CategorySlug, decimal? FromPrice, string? Currency, bool HasLiveDemo);

    public sealed record ProblemRow(string? Title, string? Detail, int? Status, string? Code, string? SuggestedSlug);
}
