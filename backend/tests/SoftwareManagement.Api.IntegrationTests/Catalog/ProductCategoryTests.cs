using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Api.IntegrationTests.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Catalog;

/// <summary>
/// The industries the catalogue is grouped by.
///
/// The list is small and changes rarely, which is exactly why the rules around it matter: the one
/// operation that can quietly take products off the public site is deleting the category they are
/// filed under, and the refusals below are what stop that happening as a side effect of tidying up.
/// </summary>
[Collection(ContentTestGroup.Name)]
public sealed class ProductCategoryTests(ContentFixture fixture)
{
    private readonly ContentFixture _fixture = fixture;

    [Fact]
    public async Task REQ_CAT_002_A_new_category_takes_the_next_position_and_the_slug_it_was_given()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var response = await editor.PostAsJsonAsync("/api/v1/admin/product-categories", new
        {
            name = $"Logistics {nonce}",
            slug = $"logistics-{nonce}",
            description = "Fleet, warehouse and dispatch.",
            iconKey = "truck",
            isPublished = true,
        });

        await CatalogArrange.EnsureCreatedAsync(response);

        var created = (await response.Content.ReadFromJsonAsync<CategoryRow>())!;
        created.Slug.Should().Be($"logistics-{nonce}");
        created.SortOrder.Should().BeGreaterThan(0);

        // A brand new category has nothing in it, and the list has to say so rather than leave the
        // count out: the count is what an editor uses to decide whether it is safe to remove.
        created.ProductCount.Should().Be(0);

        var list = await editor.GetFromJsonAsync<List<CategoryRow>>("/api/v1/admin/product-categories");
        list!.Should().Contain(c => c.Id == created.Id);
    }

    [Fact]
    public async Task REQ_CAT_002_A_category_created_without_a_slug_gets_one_made_from_its_name()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var response = await editor.PostAsJsonAsync("/api/v1/admin/product-categories", new
        {
            name = $"Field Service {nonce}",
            slug = (string?)null,
            description = (string?)null,
            iconKey = (string?)null,
            isPublished = false,
        });

        await CatalogArrange.EnsureCreatedAsync(response);

        // Nobody should have to think about addresses to add a row to a list.
        (await response.Content.ReadFromJsonAsync<CategoryRow>())!.Slug
            .Should().Be($"field-service-{nonce}");
    }

    [Fact]
    public async Task REQ_CAT_002_A_name_that_cannot_become_an_address_is_refused_with_the_rule()
    {
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var response = await editor.PostAsJsonAsync("/api/v1/admin/product-categories", new
        {
            name = "!!!",
            slug = (string?)null,
            description = (string?)null,
            iconKey = (string?)null,
            isPublished = false,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_SLUG");

        // The refusal states the rule, so the next attempt can succeed without guessing.
        body.Should().Contain("lowercase");
    }

    [Fact]
    public async Task REQ_CAT_002_The_same_address_twice_is_a_conflict_rather_than_a_second_category()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var first = await editor.PostAsJsonAsync("/api/v1/admin/product-categories", new
        {
            name = $"Hospitality {nonce}",
            slug = $"hospitality-{nonce}",
            description = (string?)null,
            iconKey = (string?)null,
            isPublished = true,
        });

        await CatalogArrange.EnsureCreatedAsync(first);

        var second = await editor.PostAsJsonAsync("/api/v1/admin/product-categories", new
        {
            name = $"Hospitality again {nonce}",
            slug = $"hospitality-{nonce}",
            description = (string?)null,
            iconKey = (string?)null,
            isPublished = true,
        });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).Should().Contain("SLUG_TAKEN");

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ProductCategories.CountAsync(c => c.Slug == $"hospitality-{nonce}")).Should().Be(1);
    }

    [Fact]
    public async Task REQ_CAT_002_A_category_holding_a_published_product_cannot_be_deleted()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var category = await CatalogArrange.CreateCategoryAsync(_fixture, $"marine-{nonce}", $"Marine {nonce}");

        var product = await CatalogArrange.CreateProductAsync(
            editor, $"Harbour {nonce}", $"harbour-{nonce}", $"marine-{nonce}");

        await CatalogArrange.BuildPublishableProductAsync(_fixture, editor, product);
        (await owner.PostAsync($"/api/v1/admin/products/{product.Id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var deleted = await owner.DeleteAsync($"/api/v1/admin/product-categories/{category.Id}");

        // Tidying a list must not be a way to take a live product off the site.
        deleted.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await deleted.Content.ReadAsStringAsync();
        body.Should().Contain("CATEGORY_IN_USE");

        // The count, so the message says how much work moving them is.
        body.Should().Contain("publishedProductCount");
    }

    [Fact]
    public async Task REQ_CAT_002_A_category_holding_only_a_draft_is_refused_too_and_says_which_case_it_is()
    {
        var nonce = CatalogArrange.Nonce();
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var category = await CatalogArrange.CreateCategoryAsync(_fixture, $"mining-{nonce}", $"Mining {nonce}");
        await CatalogArrange.CreateProductAsync(editor, $"Pit {nonce}", $"pit-{nonce}", $"mining-{nonce}");

        var deleted = await owner.DeleteAsync($"/api/v1/admin/product-categories/{category.Id}");

        deleted.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await deleted.Content.ReadAsStringAsync();
        body.Should().Contain("CATEGORY_IN_USE");

        // A draft is a different problem from a live product, and the editor is told which.
        body.Should().Contain("Draft");
    }

    [Fact]
    public async Task REQ_CAT_002_An_empty_category_is_deleted_and_stops_being_listed()
    {
        var nonce = CatalogArrange.Nonce();
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        var category = await CatalogArrange.CreateCategoryAsync(_fixture, $"retail-{nonce}", $"Retail {nonce}");

        (await owner.DeleteAsync($"/api/v1/admin/product-categories/{category.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await owner.GetFromJsonAsync<List<CategoryRow>>("/api/v1/admin/product-categories");
        list!.Should().NotContain(c => c.Id == category.Id);
    }

    [Fact]
    public async Task REQ_CAT_002_Deleting_a_category_that_is_not_there_is_a_404()
    {
        var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);

        (await owner.DeleteAsync($"/api/v1/admin/product-categories/{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task REQ_CAT_002_Only_the_roles_the_matrix_names_may_read_and_change_the_industries()
    {
        var nonce = CatalogArrange.Nonce();
        var category = await CatalogArrange.CreateCategoryAsync(_fixture, $"energy-{nonce}", $"Energy {nonce}");

        using var anonymous = _fixture.CreateClient();
        (await anonymous.GetAsync("/api/v1/admin/product-categories"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Sales reads the catalogue and does not author it (AZ-12, AZ-13).
        var sales = await _fixture.ClientAsAsync(ContentFixture.SalesEmail);
        (await sales.GetAsync("/api/v1/admin/product-categories"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await sales.PostAsJsonAsync("/api/v1/admin/product-categories", new
        {
            name = $"Sales tried {nonce}",
            slug = $"sales-tried-{nonce}",
            description = (string?)null,
            iconKey = (string?)null,
            isPublished = true,
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await sales.DeleteAsync($"/api/v1/admin/product-categories/{category.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The editor owns the catalogue, archiving included: AZ-16 grants
        // catalog.product.archive to Editor and Owner and to nobody else.
        var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        (await editor.DeleteAsync($"/api/v1/admin/product-categories/{category.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private sealed record CategoryRow(
        Guid Id,
        string Name,
        string Slug,
        string? Description,
        string? IconKey,
        int SortOrder,
        bool IsPublished,
        int ProductCount);
}
