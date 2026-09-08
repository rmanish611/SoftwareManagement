using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Api.IntegrationTests.Content;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Catalog;

/// <summary>
/// The catalogue an editor maintains: products, their features, screenshots, pricing plans and the
/// live demo. Each test asserts one rule from `docs/blueprint/04-requirements.md`, against the real
/// API and the real database.
/// </summary>
[Collection(ContentTestGroup.Name)]
public sealed class ProductCatalogTests(ContentFixture fixture)
{
    private readonly ContentFixture _fixture = fixture;

    [Fact]
    public async Task REQ_CAT_001_A_new_product_is_stored_as_a_draft_and_stays_out_of_the_public_catalogue()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var product = await CatalogArrange.CreateProductAsync(client, $"Ledger {nonce}");

        product.Status.Should().Be("Draft");
        product.CategorySlug.Should().Be("erp");

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        stored.Name.Should().Be($"Ledger {nonce}");

        using var anonymous = _fixture.CreateClient();
        var publicCards = await anonymous.GetFromJsonAsync<List<CatalogArrange.PublicCard>>("/api/v1/public/catalog/products");
        publicCards.Should().NotBeNull();
        publicCards!.Should().NotContain(card => card.Slug == product.Slug);
    }

    [Fact]
    public async Task REQ_CAT_001_A_slug_another_product_already_holds_is_refused_with_an_alternative()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var taken = $"payroll-{nonce}";
        await CatalogArrange.CreateProductAsync(client, $"Payroll {nonce}", taken);

        var second = await client.PostAsJsonAsync("/api/v1/admin/products", new
        {
            name = $"Payroll Cloud {nonce}",
            slug = taken,
            tagline = "Second product, same address.",
            summary = "A summary.",
            categorySlug = "erp",
        });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await second.Content.ReadFromJsonAsync<CatalogArrange.ProblemRow>();
        problem!.Code.Should().Be("SLUG_TAKEN");

        // The suggestion has to be usable, not just present: it is the address the editor will
        // accept with one click.
        problem.SuggestedSlug.Should().Be($"{taken}-2");
    }

    [Fact]
    public async Task REQ_CAT_002_The_public_catalogue_filtered_by_a_category_returns_only_that_categorys_published_products()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var mine = await CatalogArrange.CreateCategoryAsync(_fixture, $"vertical-{nonce}", $"Vertical {nonce}");

        var listed = await CatalogArrange.CreateProductAsync(client, $"Clinic {nonce}", categorySlug: mine.Slug);
        var elsewhere = await CatalogArrange.CreateProductAsync(client, $"Ledger {nonce}", categorySlug: "erp");
        var stillDraft = await CatalogArrange.CreateProductAsync(client, $"Unfinished {nonce}", categorySlug: mine.Slug);

        await CatalogArrange.PublishAsync(_fixture, listed.Id);
        await CatalogArrange.PublishAsync(_fixture, elsewhere.Id);

        using var anonymous = _fixture.CreateClient();
        var cards = await anonymous.GetFromJsonAsync<List<CatalogArrange.PublicCard>>(
            $"/api/v1/public/catalog/products?category={mine.Slug}");

        cards.Should().NotBeNull();
        cards!.Select(c => c.Slug).Should().Contain(listed.Slug);
        cards.Select(c => c.Slug).Should().NotContain(elsewhere.Slug);
        cards.Select(c => c.Slug).Should().NotContain(stillDraft.Slug);
        cards.Should().OnlyContain(c => c.CategorySlug == mine.Slug);
    }

    [Fact]
    public async Task REQ_CAT_002_A_category_that_still_holds_a_published_product_cannot_be_deleted()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var category = await CatalogArrange.CreateCategoryAsync(_fixture, $"doomed-{nonce}", $"Doomed {nonce}");
        var product = await CatalogArrange.CreateProductAsync(client, $"Held {nonce}", categorySlug: category.Slug);
        await CatalogArrange.PublishAsync(_fixture, product.Id);

        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);
        var response = await owner.DeleteAsync($"/api/v1/admin/product-categories/{category.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<CatalogArrange.ProblemRow>();
        problem!.Code.Should().Be("CATEGORY_IN_USE");

        // The category is still there: a refused delete must not half-happen.
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ProductCategories.AnyAsync(c => c.Id == category.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task REQ_CAT_003_A_second_feature_with_a_name_the_product_already_lists_is_refused()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(client, $"Billing {nonce}");

        await CatalogArrange.AddFeatureAsync(client, product.Id, "GST invoicing");
        await CatalogArrange.AddFeatureAsync(client, product.Id, "Recurring billing");

        var duplicate = await client.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/features", new
        {
            name = "GST invoicing",
            description = "Typed twice by mistake.",
            groupName = "Core",
            isHighlighted = false,
        });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadFromJsonAsync<CatalogArrange.ProblemRow>())!.Code.Should().Be("DUPLICATE_FEATURE");
    }

    [Fact]
    public async Task REQ_CAT_003_Reordering_features_leaves_the_stored_positions_contiguous()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(client, $"Stock {nonce}");

        var first = await CatalogArrange.AddFeatureAsync(client, product.Id, "Stock ledger");
        var second = await CatalogArrange.AddFeatureAsync(client, product.Id, "Batch tracking");
        var third = await CatalogArrange.AddFeatureAsync(client, product.Id, "Expiry alerts");
        var fourth = await CatalogArrange.AddFeatureAsync(client, product.Id, "Reorder levels");

        new[] { first, second, third, fourth }.Select(f => f.SortOrder).Should().Equal(1, 2, 3, 4);

        var reordered = await client.PutAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/features/order",
            new { orderedIds = new[] { fourth.Id, first.Id, third.Id, second.Id } });

        reordered.EnsureSuccessStatusCode();
        var rows = await reordered.Content.ReadFromJsonAsync<List<CatalogArrange.FeatureRow>>();

        rows.Should().NotBeNull();
        rows!.Select(f => f.SortOrder).Should().Equal(1, 2, 3, 4);
        rows.Select(f => f.Id).Should().Equal(fourth.Id, first.Id, third.Id, second.Id);
    }

    [Fact]
    public async Task REQ_CAT_004_A_screenshot_is_attached_in_its_own_position_in_the_gallery()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(client, $"Gallery {nonce}");

        var firstAsset = await CatalogArrange.AddMediaAssetAsync(_fixture, nonce + "a");
        var secondAsset = await CatalogArrange.AddMediaAssetAsync(_fixture, nonce + "b");

        var first = await client.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/screenshots",
            new { mediaAssetId = firstAsset, caption = "The dashboard" });
        var second = await client.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/screenshots",
            new { mediaAssetId = secondAsset, caption = "The invoice screen" });

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        (await first.Content.ReadFromJsonAsync<CatalogArrange.ScreenshotRow>())!.SortOrder.Should().Be(1);
        (await second.Content.ReadFromJsonAsync<CatalogArrange.ScreenshotRow>())!.SortOrder.Should().Be(2);
    }

    [Fact]
    public async Task REQ_CAT_004_A_screenshot_pointing_at_an_image_nobody_uploaded_is_refused()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(client, $"Ghost {nonce}");

        var response = await client.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/screenshots",
            new { mediaAssetId = Guid.NewGuid(), caption = "Nothing behind this" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<CatalogArrange.ProblemRow>())!.Code.Should().Be("UNKNOWN_MEDIA");
    }

    [Fact]
    public async Task REQ_CAT_005_A_plan_priced_at_4999_rupees_stores_exactly_that_amount_with_its_currency()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(client, $"Priced {nonce}");

        var plan = await CatalogArrange.AddPlanAsync(client, product.Id, "Growth", 4999.00m);

        plan.Price.Should().Be(4999.00m);
        plan.Currency.Should().Be("INR");

        // The stored value matters more than the response: a rounding loss would show up here.
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.PricingPlans.AsNoTracking().FirstAsync(p => p.Id == plan.Id);
        stored.Price.Should().Be(4999.00m);
        stored.Currency.Should().Be("INR");
    }

    [Fact]
    public async Task REQ_CAT_005_A_plan_priced_below_zero_is_refused()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(client, $"Negative {nonce}");

        var response = await client.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/plans", new
        {
            name = "Impossible",
            price = -1m,
            currency = "INR",
            billingPeriod = "Monthly",
            includedSeats = 1,
            isFreeTier = false,
            isRecommended = false,
            setupFee = 0m,
            isPublished = false,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<CatalogArrange.ProblemRow>())!.Code.Should().Be("INVALID_PRICE");
    }

    [Fact]
    public async Task REQ_CAT_005_A_plan_priced_at_zero_that_does_not_say_it_is_free_is_refused()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(client, $"Zero {nonce}");

        var response = await client.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/plans", new
        {
            name = "Unfinished",
            price = 0m,
            currency = "INR",
            billingPeriod = "Monthly",
            includedSeats = 1,
            isFreeTier = false,
            isRecommended = false,
            setupFee = 0m,
            isPublished = true,
        });

        // An empty price box must never be published as though the product were free.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<CatalogArrange.ProblemRow>())!.Code.Should().Be("INVALID_PRICE");
    }

    [Fact]
    public async Task REQ_CAT_006_Recommending_a_second_plan_clears_the_first_and_leaves_exactly_one()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(client, $"Tiers {nonce}");

        var planA = await CatalogArrange.AddPlanAsync(client, product.Id, "Starter", 999m, isRecommended: true);
        var planB = await CatalogArrange.AddPlanAsync(client, product.Id, "Growth", 2999m);

        planA.IsRecommended.Should().BeTrue();
        planB.IsRecommended.Should().BeFalse();

        var moved = await client.PutAsJsonAsync($"/api/v1/admin/products/{product.Id}/plans/{planB.Id}", new
        {
            name = "Growth",
            price = 2999m,
            currency = "INR",
            billingPeriod = "Monthly",
            includedSeats = 5,
            isFreeTier = false,
            isRecommended = true,
            setupFee = 0m,
            isPublished = true,
        });

        moved.EnsureSuccessStatusCode();
        (await moved.Content.ReadFromJsonAsync<CatalogArrange.PlanRow>())!.IsRecommended.Should().BeTrue();

        (await CatalogArrange.CountRecommendedAsync(_fixture, product.Id)).Should().Be(1);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.PricingPlans.AsNoTracking().FirstAsync(p => p.Id == planA.Id)).IsRecommended.Should().BeFalse();
    }

    [Fact]
    public async Task REQ_CAT_006_A_yearly_price_above_twelve_monthly_payments_is_refused()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(client, $"Yearly {nonce}");

        await CatalogArrange.AddPlanAsync(client, product.Id, "Monthly", 1000m);

        var response = await client.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/plans", new
        {
            name = "Annual",
            price = 15000m,
            currency = "INR",
            billingPeriod = "Yearly",
            includedSeats = 5,
            isFreeTier = false,
            isRecommended = false,
            setupFee = 0m,
            isPublished = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<CatalogArrange.ProblemRow>())!.Code.Should().Be("IMPLAUSIBLE_PRICE");

        // A yearly plan at or below twelve monthly payments is the ordinary case and still saves.
        var sensible = await CatalogArrange.AddPlanAsync(client, product.Id, "Annual", 10000m, billingPeriod: "Yearly");
        sensible.Price.Should().Be(10000m);
    }

    [Fact]
    public async Task REQ_CAT_007_A_feature_marked_limited_without_the_limit_itself_is_refused()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(client, $"Limits {nonce}");

        var feature = await CatalogArrange.AddFeatureAsync(client, product.Id, "Included users");
        var plan = await CatalogArrange.AddPlanAsync(client, product.Id, "Starter", 999m);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/plans/{plan.Id}/features",
            new { productFeatureId = feature.Id, availability = "Limited", limitValue = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<CatalogArrange.ProblemRow>())!.Code.Should().Be("LIMIT_REQUIRED");
    }

    [Fact]
    public async Task REQ_CAT_007_Every_cell_of_the_plan_comparison_carries_a_definite_answer()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(client, $"Compare {nonce}");

        var features = new[]
        {
            await CatalogArrange.AddFeatureAsync(client, product.Id, "Users"),
            await CatalogArrange.AddFeatureAsync(client, product.Id, "Branches"),
            await CatalogArrange.AddFeatureAsync(client, product.Id, "Priority support"),
        };

        var plans = new[]
        {
            await CatalogArrange.AddPlanAsync(client, product.Id, "Starter", 999m),
            await CatalogArrange.AddPlanAsync(client, product.Id, "Growth", 2999m),
        };

        string[] availabilities = ["Included", "Limited", "NotIncluded"];

        foreach (var plan in plans)
        {
            for (var index = 0; index < features.Length; index++)
            {
                var availability = availabilities[index];
                var response = await client.PutAsJsonAsync(
                    $"/api/v1/admin/products/{product.Id}/plans/{plan.Id}/features",
                    new
                    {
                        productFeatureId = features[index].Id,
                        availability,
                        limitValue = availability == "Limited" ? "5 branches" : null,
                    });

                response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            }
        }

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var planIds = plans.Select(p => p.Id).ToArray();
        var cells = await db.PlanFeatures.AsNoTracking()
            .Where(f => planIds.Contains(f.PricingPlanId))
            .ToListAsync();

        // Two plans by three features is six cells, and none of them is blank.
        cells.Should().HaveCount(6);
        cells.Where(c => c.Availability == FeatureAvailability.Limited)
            .Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.LimitValue));
        cells.Where(c => c.Availability != FeatureAvailability.Limited)
            .Should().OnlyContain(c => c.LimitValue == null);
    }

    [Fact]
    public async Task REQ_CAT_008_The_demo_is_recorded_and_its_credentials_stay_out_of_the_product_response()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(client, $"Demoed {nonce}");

        var secret = $"demo-pass-{nonce}";

        var saved = await client.PutAsJsonAsync($"/api/v1/admin/products/{product.Id}/demo", new
        {
            url = "https://demo.example.com/erp",
            demoUsername = "demo",
            demoPassword = secret,
            isEnabled = true,
        });

        saved.EnsureSuccessStatusCode();

        var savedBody = await saved.Content.ReadAsStringAsync();
        savedBody.Should().NotContain(secret);

        var read = await client.GetAsync($"/api/v1/admin/products/{product.Id}");
        var readBody = await read.Content.ReadAsStringAsync();
        readBody.Should().Contain("https://demo.example.com/erp");
        readBody.Should().NotContain(secret);
    }

    [Fact]
    public async Task REQ_CAT_008_Demo_credentials_are_returned_only_to_a_caller_holding_the_demo_permission()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(editor, $"Guarded {nonce}");

        var secret = $"demo-pass-{nonce}";

        (await editor.PutAsJsonAsync($"/api/v1/admin/products/{product.Id}/demo", new
        {
            url = "https://demo.example.com/hms",
            demoUsername = "demo",
            demoPassword = secret,
            isEnabled = true,
        })).EnsureSuccessStatusCode();

        // AZ-19 grants the demo sign-in to the Owner, the Editor and Sales, who quote it to a
        // prospect on a call, and withholds it from the Auditor, who audits money rather than
        // demonstrations.
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);
        var allowed = await owner.GetAsync($"/api/v1/admin/products/{product.Id}/demo/credentials");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await allowed.Content.ReadAsStringAsync()).Should().Contain(secret);

        var sales = await _fixture.ClientAsAsync(ContentFixture.SalesEmail);
        (await sales.GetAsync($"/api/v1/admin/products/{product.Id}/demo/credentials")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var auditor = await _fixture.ClientAsAsync(ContentFixture.AuditorEmail);
        var refused = await auditor.GetAsync($"/api/v1/admin/products/{product.Id}/demo/credentials");
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).Should().NotContain(secret);
    }

    [Fact]
    public async Task REQ_CAT_001_Sales_reads_the_catalogue_but_cannot_author_it()
    {
        var nonce = CatalogArrange.Nonce();
        var sales = await _fixture.ClientAsAsync(ContentFixture.SalesEmail);

        // Sales sells what the catalogue holds, so it reads products (AZ-12) and does not write
        // them (AZ-13). Both halves are asserted, because a role that can do neither is as wrong as
        // one that can do both.
        (await sales.GetAsync("/api/v1/admin/products")).StatusCode.Should().Be(HttpStatusCode.OK);

        var create = await sales.PostAsJsonAsync("/api/v1/admin/products", new
        {
            name = $"Should not exist {nonce}",
            slug = $"should-not-exist-{nonce}",
            tagline = "No",
            summary = "No",
            categorySlug = "erp",
        });

        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Products.AnyAsync(p => p.Slug == $"should-not-exist-{nonce}")).Should().BeFalse();

        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(editor, $"Plans {nonce}");

        var plan = await sales.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/plans", new
        {
            name = "Sales cannot price this",
            price = 1m,
            currency = "INR",
            billingPeriod = "Monthly",
            includedSeats = 1,
            isFreeTier = false,
            isRecommended = false,
            setupFee = 0m,
            isPublished = false,
        });

        plan.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task REQ_CAT_005_The_public_card_quotes_the_cheapest_published_paid_plan()
    {
        var nonce = CatalogArrange.Nonce();
        var client = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var category = await CatalogArrange.CreateCategoryAsync(_fixture, $"pricing-{nonce}", $"Pricing {nonce}");
        var product = await CatalogArrange.CreateProductAsync(client, $"From {nonce}", categorySlug: category.Slug);

        await CatalogArrange.AddPlanAsync(client, product.Id, "Free", 0m, isFreeTier: true);
        await CatalogArrange.AddPlanAsync(client, product.Id, "Starter", 1499m);
        await CatalogArrange.AddPlanAsync(client, product.Id, "Growth", 4999m);
        await CatalogArrange.AddPlanAsync(client, product.Id, "Hidden", 99m, isPublished: false);

        await CatalogArrange.PublishAsync(_fixture, product.Id);

        using var anonymous = _fixture.CreateClient();
        var cards = await anonymous.GetFromJsonAsync<List<CatalogArrange.PublicCard>>(
            $"/api/v1/public/catalog/products?category={category.Slug}");

        var card = cards!.Single(c => c.Slug == product.Slug);

        // Not the free tier, and not the unpublished plan: "from" has to be a price a visitor can
        // actually buy today (BR-CAT-05).
        card.FromPrice.Should().Be(1499m);
        card.Currency.Should().Be("INR");
    }
}
