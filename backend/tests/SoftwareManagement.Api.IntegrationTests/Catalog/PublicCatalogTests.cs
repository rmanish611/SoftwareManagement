using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Api.IntegrationTests.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Catalog;

/// <summary>
/// The catalogue a visitor reads, and the gate that decides what reaches it.
///
/// The rule under most of these tests is the same one: a product is either presentable or it is not
/// on the site. The rest is about not telling a stranger what exists behind the sign-in.
/// </summary>
[Collection(ContentTestGroup.Name)]
public sealed class PublicCatalogTests(ContentFixture fixture)
{
    private readonly ContentFixture _fixture = fixture;

    [Fact]
    public async Task REQ_CAT_009_Publishing_is_refused_below_the_thresholds_and_the_response_names_the_failing_counts()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var product = await CatalogArrange.CreateProductAsync(editor, $"Half built {nonce}");
        await CatalogArrange.AddFeatureAsync(editor, product.Id, "One");
        await CatalogArrange.AddFeatureAsync(editor, product.Id, "Two");

        var refused = await owner.PostAsync($"/api/v1/admin/products/{product.Id}/publish", null);

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await refused.Content.ReadAsStringAsync();
        body.Should().Contain("NOT_READY_TO_PUBLISH");

        // The counts, not just a refusal: an editor has to know which threshold they are short of.
        body.Should().Contain("features");
        body.Should().Contain("screenshots");
        body.Should().Contain("pricing plans");

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id)).PublishedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task REQ_CAT_009_A_complete_product_publishes_and_appears_in_the_public_catalogue()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var product = await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, $"Complete {nonce}", nonce);

        var published = await owner.PostAsync($"/api/v1/admin/products/{product.Id}/publish", null);
        published.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var anonymous = _fixture.CreateClient();
        var cards = await anonymous.GetFromJsonAsync<List<CatalogArrange.PublicCard>>("/api/v1/public/products");
        cards!.Select(c => c.Slug).Should().Contain(product.Slug);

        var page = await anonymous.GetAsync($"/api/v1/public/products/{product.Slug}");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task REQ_CAT_010_A_draft_products_public_address_is_a_404_and_never_a_403()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(editor, $"Hidden {nonce}");

        using var anonymous = _fixture.CreateClient();
        var response = await anonymous.GetAsync($"/api/v1/public/products/{product.Slug}");

        // 403 would confirm the address exists, which is the leak the rule is about.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task REQ_CAT_010_The_catalogue_lists_published_products_only_with_their_lowest_paid_price()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);
        var category = await CatalogArrange.CreateCategoryAsync(_fixture, $"listed-{nonce}", $"Listed {nonce}");

        var live = await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, $"Live {nonce}", nonce, category.Slug);
        await CatalogArrange.AddPlanAsync(editor, live.Id, "Free", 0m, isFreeTier: true);
        await CatalogArrange.AddPlanAsync(editor, live.Id, "Cheapest hidden", 99m, isPublished: false);
        (await owner.PostAsync($"/api/v1/admin/products/{live.Id}/publish", null)).EnsureSuccessStatusCode();

        var draft = await CatalogArrange.CreateProductAsync(editor, $"Draft {nonce}", categorySlug: category.Slug);

        using var anonymous = _fixture.CreateClient();
        var cards = await anonymous.GetFromJsonAsync<List<CatalogArrange.PublicCard>>(
            $"/api/v1/public/products?category={category.Slug}");

        cards.Should().NotBeNull();
        cards!.Select(c => c.Slug).Should().Contain(live.Slug);
        cards.Select(c => c.Slug).Should().NotContain(draft.Slug);

        // The free tier and the unpublished plan are both excluded from "from Rs. X": one is not a
        // price and the other is not on sale (BR-CAT-05).
        cards.Single(c => c.Slug == live.Slug).FromPrice.Should().Be(1999m);
    }

    [Fact]
    public async Task REQ_CAT_010_A_request_for_a_hundred_thousand_products_returns_at_most_a_hundred()
    {
        using var anonymous = _fixture.CreateClient();
        var response = await anonymous.GetAsync("/api/v1/public/products?pageSize=100000");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var cards = await response.Content.ReadFromJsonAsync<List<CatalogArrange.PublicCard>>();
        cards!.Count.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task REQ_CAT_010_Opening_a_product_page_counts_the_view_against_that_path()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var product = await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, $"Counted {nonce}", nonce);
        (await owner.PostAsync($"/api/v1/admin/products/{product.Id}/publish", null)).EnsureSuccessStatusCode();

        var path = $"/products/{product.Slug}";

        using var anonymous = _fixture.CreateClient();
        (await anonymous.GetAsync($"/api/v1/public/products/{product.Slug}")).EnsureSuccessStatusCode();
        (await anonymous.GetAsync($"/api/v1/public/products/{product.Slug}")).EnsureSuccessStatusCode();

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stat = await db.PageViewStats.AsNoTracking().SingleAsync(s => s.Path == path);

        // Two views, one row: the second request incremented rather than inserting a second row.
        stat.Views.Should().Be(2);
    }

    [Fact]
    public async Task REQ_CAT_011_The_product_page_carries_the_features_screenshots_plans_and_faq()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var product = await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, $"Full page {nonce}", nonce);

        (await editor.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/faqs", new
        {
            question = "Do you migrate our existing data?",
            answer = "Yes, and the first migration run is included in the setup fee.",
            isPublished = true,
        })).EnsureSuccessStatusCode();

        (await editor.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/faqs", new
        {
            question = "Is this question still being written?",
            answer = "It is, so it must not appear on the page.",
            isPublished = false,
        })).EnsureSuccessStatusCode();

        (await owner.PostAsync($"/api/v1/admin/products/{product.Id}/publish", null)).EnsureSuccessStatusCode();

        using var anonymous = _fixture.CreateClient();
        var page = await anonymous.GetFromJsonAsync<CatalogArrange.PublicPage>($"/api/v1/public/products/{product.Slug}");

        page!.Features.Should().HaveCountGreaterThanOrEqualTo(3);
        page.Screenshots.Should().NotBeEmpty();
        page.Plans.Should().NotBeEmpty();
        page.Faqs.Should().ContainSingle();
        page.Faqs[0].Question.Should().Contain("migrate");
    }

    [Fact]
    public async Task REQ_CAT_011_The_demo_disappears_from_the_page_once_it_has_failed_twice()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var product = await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, $"Demoed page {nonce}", nonce);

        (await editor.PutAsJsonAsync($"/api/v1/admin/products/{product.Id}/demo", new
        {
            url = "https://demo.example.com/erp",
            demoUsername = "demo",
            demoPassword = $"demo-pass-{nonce}",
            isEnabled = true,
        })).EnsureSuccessStatusCode();

        (await owner.PostAsync($"/api/v1/admin/products/{product.Id}/publish", null)).EnsureSuccessStatusCode();

        using var anonymous = _fixture.CreateClient();
        var healthy = await anonymous.GetFromJsonAsync<CatalogArrange.PublicPage>($"/api/v1/public/products/{product.Slug}");
        healthy!.Demo.Should().NotBeNull();

        await CatalogArrange.MarkDemoDownAsync(_fixture, product.Id);

        var afterFailure = await anonymous.GetFromJsonAsync<CatalogArrange.PublicPage>($"/api/v1/public/products/{product.Slug}");

        // The demo button is gone; the page and its enquiry route are not.
        afterFailure!.Demo.Should().BeNull();
        afterFailure.Plans.Should().NotBeEmpty();
    }

    [Fact]
    public async Task REQ_CAT_012_Searching_a_word_that_appears_only_in_one_products_features_returns_only_that_product()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var wanted = await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, $"Pharmacy {nonce}", nonce + "a");
        var other = await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, $"Ledger {nonce}", nonce + "b");

        var term = $"radiology{nonce}";
        await CatalogArrange.AddFeatureAsync(editor, wanted.Id, term);

        (await owner.PostAsync($"/api/v1/admin/products/{wanted.Id}/publish", null)).EnsureSuccessStatusCode();
        (await owner.PostAsync($"/api/v1/admin/products/{other.Id}/publish", null)).EnsureSuccessStatusCode();

        using var anonymous = _fixture.CreateClient();
        var found = await anonymous.GetFromJsonAsync<List<CatalogArrange.PublicCard>>($"/api/v1/public/products?search={term}");

        found!.Should().ContainSingle();
        found[0].Slug.Should().Be(wanted.Slug);
    }

    [Fact]
    public async Task REQ_CAT_012_A_search_that_matches_nothing_is_an_empty_list_rather_than_an_error()
    {
        using var anonymous = _fixture.CreateClient();
        var response = await anonymous.GetAsync($"/api/v1/public/products?search=nothing-matches-{CatalogArrange.Nonce()}");

        // The page turns this into an invitation to get in touch. An error page would lose the
        // visitor entirely (REQ-CAT-012).
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<List<CatalogArrange.PublicCard>>())!.Should().BeEmpty();
    }

    [Fact]
    public async Task REQ_CAT_013_The_plan_table_omits_unpublished_plans_and_leaves_no_cell_blank()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var product = await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, $"Compared {nonce}", nonce);
        var detail = await editor.GetFromJsonAsync<CatalogArrange.ProductDetailRow>($"/api/v1/admin/products/{product.Id}");

        var growth = await CatalogArrange.AddPlanAsync(editor, product.Id, "Growth", 4999m, isRecommended: true);
        await CatalogArrange.AddPlanAsync(editor, product.Id, "Not on sale", 100m, isPublished: false);

        (await editor.PutAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/plans/{growth.Id}/features",
            new { productFeatureId = detail!.Features[0].Id, availability = "Limited", limitValue = "10 users" }))
            .EnsureSuccessStatusCode();

        (await owner.PostAsync($"/api/v1/admin/products/{product.Id}/publish", null)).EnsureSuccessStatusCode();

        using var anonymous = _fixture.CreateClient();
        var page = await anonymous.GetFromJsonAsync<CatalogArrange.PublicPage>($"/api/v1/public/products/{product.Slug}");

        page!.Plans.Select(p => p.Name).Should().NotContain("Not on sale");
        page.Plans.Should().ContainSingle(p => p.IsRecommended);

        // Every plan answers for every feature, including the pairs nobody set: a blank cell in a
        // comparison table reads as an evasion.
        foreach (var plan in page.Plans)
        {
            plan.Cells.Should().HaveCount(page.Features.Count);
            plan.Cells.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Availability));
        }

        var limited = page.Plans.Single(p => p.Name == "Growth").Cells.Single(c => c.LimitValue != null);
        limited.LimitValue.Should().Be("10 users");
    }

    [Fact]
    public async Task REQ_CAT_014_Only_published_faq_items_reach_the_page_and_they_keep_their_order()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var product = await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, $"Questions {nonce}", nonce);

        foreach (var (question, published) in new[]
        {
            ("First question", true),
            ("Second question", true),
            ("Third question", true),
            ("Unfinished question", false),
        })
        {
            (await editor.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/faqs", new
            {
                question,
                answer = $"The answer to the {question.ToLowerInvariant()}.",
                isPublished = published,
            })).EnsureSuccessStatusCode();
        }

        (await owner.PostAsync($"/api/v1/admin/products/{product.Id}/publish", null)).EnsureSuccessStatusCode();

        using var anonymous = _fixture.CreateClient();
        var page = await anonymous.GetFromJsonAsync<CatalogArrange.PublicPage>($"/api/v1/public/products/{product.Slug}");

        page!.Faqs.Select(f => f.Question).Should().Equal("First question", "Second question", "Third question");
    }

    [Fact]
    public async Task REQ_CAT_014_A_question_saved_without_an_answer_is_refused()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(editor, $"Unanswered {nonce}");

        var response = await editor.PostAsJsonAsync($"/api/v1/admin/products/{product.Id}/faqs", new
        {
            question = "Why is there no answer here?",
            answer = "",
            isPublished = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<CatalogArrange.ProblemRow>())!.Code.Should().Be("FAQ_INCOMPLETE");
    }

    [Fact]
    public async Task REQ_CAT_015_Archiving_removes_a_product_from_the_catalogue_and_keeps_its_address_claimed()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var product = await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, $"Retired {nonce}", nonce);
        (await owner.PostAsync($"/api/v1/admin/products/{product.Id}/publish", null)).EnsureSuccessStatusCode();

        (await owner.PostAsync($"/api/v1/admin/products/{product.Id}/archive", null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        using var anonymous = _fixture.CreateClient();
        var cards = await anonymous.GetFromJsonAsync<List<CatalogArrange.PublicCard>>("/api/v1/public/products");
        cards!.Select(c => c.Slug).Should().NotContain(product.Slug);

        // The address is not free afterwards: a new product may not take it, because the old one is
        // still in bookmarks, in search results and possibly in a signed quote (BR-CAT-07).
        var reuse = await editor.PostAsJsonAsync("/api/v1/admin/products", new
        {
            name = $"Successor {nonce}",
            slug = product.Slug,
            tagline = "Trying to take the retired address.",
            summary = "Must be refused.",
            categorySlug = "erp",
        });

        reuse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await reuse.Content.ReadFromJsonAsync<CatalogArrange.ProblemRow>())!.Code.Should().Be("SLUG_TAKEN");
    }

    [Fact]
    public async Task REQ_CAT_015_A_product_that_has_been_public_cannot_be_deleted_only_archived()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var published = await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, $"Published once {nonce}", nonce);
        (await owner.PostAsync($"/api/v1/admin/products/{published.Id}/publish", null)).EnsureSuccessStatusCode();

        var refused = await owner.DeleteAsync($"/api/v1/admin/products/{published.Id}");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<CatalogArrange.ProblemRow>())!.Code.Should().Be("PRODUCT_IN_USE");

        // A draft nobody ever saw is a different case: it was a mistake, and deleting it costs
        // nothing outside this system.
        var neverPublic = await CatalogArrange.CreateProductAsync(editor, $"Created by mistake {nonce}");
        (await owner.DeleteAsync($"/api/v1/admin/products/{neverPublic.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Products.AnyAsync(p => p.Id == published.Id)).Should().BeTrue();
        (await db.Products.AnyAsync(p => p.Id == neverPublic.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task REQ_CAT_009_The_readiness_report_says_what_is_missing_before_the_button_is_pressed()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var product = await CatalogArrange.CreateProductAsync(editor, $"Checking {nonce}");

        var empty = await editor.GetFromJsonAsync<CatalogArrange.ReadinessRow>($"/api/v1/admin/products/{product.Id}/readiness");
        empty!.IsReady.Should().BeFalse();
        empty.Shortfalls.Should().Contain(s => s.What == "features" && s.Has == 0 && s.Needs == 3);

        await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, product);

        var ready = await editor.GetFromJsonAsync<CatalogArrange.ReadinessRow>($"/api/v1/admin/products/{product.Id}/readiness");
        ready!.IsReady.Should().BeTrue();
        ready.Shortfalls.Should().BeEmpty();
    }
}
