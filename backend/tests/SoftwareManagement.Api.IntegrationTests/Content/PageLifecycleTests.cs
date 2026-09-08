using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Content;

/// <summary>
/// The publishing lifecycle: draft, publish, edit, republish, schedule, version and restore.
/// </summary>
[Trait("Category", "Integration")]
[Collection(ContentTestGroup.Name)]
public sealed class PageLifecycleTests(ContentFixture fixture)
{
    private readonly ContentFixture _fixture = fixture;

    private static string UniqueTitle(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..24];

    [Fact]
    public async Task REQ_SITE_001_CreatesAPageAsADraftThatThePublicCannotSee()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var title = UniqueTitle("Draft page");

        var created = await editor.PostAsJsonAsync("/api/v1/admin/pages", new
        {
            title,
            slug = (string?)null,
            pageType = "Custom",
            body = "<p>Not published yet.</p>",
            metaTitle = title,
            metaDescription = (string?)null,
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var page = await created.Content.ReadFromJsonAsync<ContentFixture.PageRow>();
        page!.Status.Should().Be("Draft");

        using var anonymous = _fixture.CreateClient();
        var publicView = await anonymous.GetAsync(new Uri($"/api/v1/public/pages/{page.Slug}", UriKind.Relative));

        publicView.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a draft is indistinguishable from a page that never existed");
    }

    [Fact]
    public async Task REQ_SITE_001_Returns422_ForASlugLongerThanTheColumn()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var response = await editor.PostAsJsonAsync("/api/v1/admin/pages", new
        {
            title = "Too long",
            slug = new string('a', 130),
            pageType = "Custom",
            body = (string?)null,
            metaTitle = (string?)null,
            metaDescription = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("120");
    }

    [Fact]
    public async Task REQ_SITE_001_Returns409AndSuggestsAnAlternative_WhenTheSlugIsTaken()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var slug = "duplicate-" + Guid.NewGuid().ToString("N")[..8];

        await ContentFixture.CreateDraftPageAsync(editor, "First", slug);

        var second = await editor.PostAsJsonAsync("/api/v1/admin/pages", new
        {
            title = "Second",
            slug,
            pageType = "Custom",
            body = (string?)null,
            metaTitle = (string?)null,
            metaDescription = (string?)null,
        });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).Should().Contain($"{slug}-2");
    }

    [Fact]
    public async Task REQ_SITE_003_PublishesAPageAndServesItToThePublic()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var id = await ContentFixture.CreateDraftPageAsync(editor, "About us", "about-" + Guid.NewGuid().ToString("N")[..8]);

        var published = await editor.PostAsync(new Uri($"/api/v1/admin/pages/{id}/publish", UriKind.Relative), null);
        published.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await editor.GetFromJsonAsync<ContentFixture.PageRow>($"/api/v1/admin/pages/{id}");

        using var anonymous = _fixture.CreateClient();
        var publicView = await anonymous.GetAsync(new Uri($"/api/v1/public/pages/{detail!.Slug}", UriKind.Relative));

        publicView.StatusCode.Should().Be(HttpStatusCode.OK);
        (await publicView.Content.ReadAsStringAsync()).Should().Contain("About us");
    }

    [Fact]
    public async Task REQ_SITE_003_WritesAContentVersionOnEveryPublish()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var id = await ContentFixture.CreateDraftPageAsync(editor, "Versioned", "versioned-" + Guid.NewGuid().ToString("N")[..8]);

        await editor.PostAsync(new Uri($"/api/v1/admin/pages/{id}/publish", UriKind.Relative), null);

        var versions = await editor.GetFromJsonAsync<List<VersionRow>>($"/api/v1/admin/pages/{id}/versions");

        versions.Should().NotBeNull();
        versions!.Should().ContainSingle();
        versions[0].VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task REQ_SITE_004_EditingALivePageKeepsThePublicOnTheLastPublishedVersion()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var slug = "modified-" + Guid.NewGuid().ToString("N")[..8];
        var id = await ContentFixture.CreateDraftPageAsync(editor, "Original title", slug);

        await editor.PostAsync(new Uri($"/api/v1/admin/pages/{id}/publish", UriKind.Relative), null);

        var edited = await editor.PutAsJsonAsync($"/api/v1/admin/pages/{id}", new
        {
            title = "Half finished rewrite",
            slug = (string?)null,
            body = "<p>Still being written.</p>",
            metaTitle = (string?)null,
            metaDescription = (string?)null,
        });

        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        (await edited.Content.ReadFromJsonAsync<ContentFixture.PageRow>())!.Status.Should().Be("Modified");

        using var anonymous = _fixture.CreateClient();
        var publicBody = await anonymous.GetStringAsync(new Uri($"/api/v1/public/pages/{slug}", UriKind.Relative));

        publicBody.Should().Contain("Original title");
        publicBody.Should().NotContain("Half finished rewrite",
            "the public keeps seeing the published version until the draft is published");
    }

    [Fact]
    public async Task REQ_SITE_004_PublishingTheDraftReplacesWhatThePublicSees()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var slug = "republish-" + Guid.NewGuid().ToString("N")[..8];
        var id = await ContentFixture.CreateDraftPageAsync(editor, "Before", slug);

        await editor.PostAsync(new Uri($"/api/v1/admin/pages/{id}/publish", UriKind.Relative), null);
        await editor.PutAsJsonAsync($"/api/v1/admin/pages/{id}", new
        {
            title = "After",
            slug = (string?)null,
            body = "<p>Finished.</p>",
            metaTitle = (string?)null,
            metaDescription = (string?)null,
        });
        await editor.PostAsync(new Uri($"/api/v1/admin/pages/{id}/publish", UriKind.Relative), null);

        using var anonymous = _fixture.CreateClient();
        var publicBody = await anonymous.GetStringAsync(new Uri($"/api/v1/public/pages/{slug}", UriKind.Relative));

        publicBody.Should().Contain("After");
    }

    [Fact]
    public async Task REQ_SITE_005_RestoreCreatesANewVersionAndKeepsTheOldOnes()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var id = await ContentFixture.CreateDraftPageAsync(editor, "Restorable", "restorable-" + Guid.NewGuid().ToString("N")[..8]);

        await editor.PostAsync(new Uri($"/api/v1/admin/pages/{id}/publish", UriKind.Relative), null);
        await editor.PutAsJsonAsync($"/api/v1/admin/pages/{id}", new
        {
            title = "Second title",
            slug = (string?)null,
            body = "<p>Second.</p>",
            metaTitle = (string?)null,
            metaDescription = (string?)null,
        });
        await editor.PostAsync(new Uri($"/api/v1/admin/pages/{id}/publish", UriKind.Relative), null);

        var restore = await editor.PostAsync(new Uri($"/api/v1/admin/pages/{id}/versions/1/restore", UriKind.Relative), null);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var versions = await editor.GetFromJsonAsync<List<VersionRow>>($"/api/v1/admin/pages/{id}/versions");
        versions!.Count.Should().Be(3, "restoring adds a version rather than rewinding the list");

        var page = await editor.GetFromJsonAsync<ContentFixture.PageRow>($"/api/v1/admin/pages/{id}");
        page!.Title.Should().Be("Restorable", "the restored content is the version that was asked for");
    }

    [Fact]
    public async Task REQ_SITE_005_Returns404_ForAVersionThatDoesNotExist()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var id = await ContentFixture.CreateDraftPageAsync(editor, "No versions", "noversions-" + Guid.NewGuid().ToString("N")[..8]);

        var restore = await editor.PostAsync(new Uri($"/api/v1/admin/pages/{id}/versions/99/restore", UriKind.Relative), null);

        restore.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task REQ_SITE_006_StoresTheEditorsLocalTimeAsTheCorrectUtcInstant()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var id = await ContentFixture.CreateDraftPageAsync(editor, "Scheduled", "scheduled-" + Guid.NewGuid().ToString("N")[..8]);

        // Noon tomorrow, in the editor's timezone (India Standard Time, UTC+5:30).
        var localNoon = DateTime.UtcNow.AddDays(1).Date.AddHours(12);

        var response = await editor.PostAsJsonAsync($"/api/v1/admin/pages/{id}/schedule", new
        {
            publishAtLocal = localNoon,
            unpublishAtLocal = (DateTime?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ScheduleRow>();

        body!.EditorTimeZone.Should().Be("India Standard Time");
        body.PublishAtUtc.Hour.Should().Be(6, "noon in India Standard Time is 06:30 UTC");
        body.PublishAtUtc.Minute.Should().Be(30);
    }

    [Fact]
    public async Task REQ_SITE_006_Returns422_WhenTheScheduledTimeIsTooSoon()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var id = await ContentFixture.CreateDraftPageAsync(editor, "Too soon", "toosoon-" + Guid.NewGuid().ToString("N")[..8]);

        var response = await editor.PostAsJsonAsync($"/api/v1/admin/pages/{id}/schedule", new
        {
            publishAtLocal = DateTime.UtcNow.AddMinutes(2),
            unpublishAtLocal = (DateTime?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("5 minutes");
    }

    [Fact]
    public async Task REQ_SITE_006_Returns422_WhenTheUnpublishTimeComesFirst()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var id = await ContentFixture.CreateDraftPageAsync(editor, "Backwards", "backwards-" + Guid.NewGuid().ToString("N")[..8]);

        var publishAt = DateTime.UtcNow.AddDays(2);

        var response = await editor.PostAsJsonAsync($"/api/v1/admin/pages/{id}/schedule", new
        {
            publishAtLocal = publishAt,
            unpublishAtLocal = publishAt.AddHours(-1),
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task REQ_SITE_007_ReportsEachFailureIndividuallyInABulkPublish()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var first = await ContentFixture.CreateDraftPageAsync(editor, "Bulk one", "bulk1-" + Guid.NewGuid().ToString("N")[..8]);
        var second = await ContentFixture.CreateDraftPageAsync(editor, "Bulk two", "bulk2-" + Guid.NewGuid().ToString("N")[..8]);
        var missing = Guid.NewGuid();

        var response = await editor.PostAsJsonAsync("/api/v1/admin/pages/bulk-publish", new { pageIds = new[] { first, second, missing } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var outcomes = await response.Content.ReadFromJsonAsync<List<BulkRow>>();

        outcomes.Should().NotBeNull();
        outcomes!.Count(o => o.Outcome == "Published").Should().Be(2);
        outcomes.Count(o => o.Outcome == "NotFound").Should().Be(1,
            "one bad id must not stop the others from publishing");
    }

    [Fact]
    public async Task REQ_SITE_007_Returns400_WhenNothingIsSelected()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var response = await editor.PostAsJsonAsync("/api/v1/admin/pages/bulk-publish", new { pageIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task REQ_SITE_011_LeavesA301BehindWhenAPublishedPageIsRenamed()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var oldSlug = "old-address-" + Guid.NewGuid().ToString("N")[..8];
        var newSlug = "new-address-" + Guid.NewGuid().ToString("N")[..8];

        var id = await ContentFixture.CreateDraftPageAsync(editor, "Renamed page", oldSlug);
        await editor.PostAsync(new Uri($"/api/v1/admin/pages/{id}/publish", UriKind.Relative), null);

        await editor.PutAsJsonAsync($"/api/v1/admin/pages/{id}", new
        {
            title = "Renamed page",
            slug = newSlug,
            body = (string?)null,
            metaTitle = (string?)null,
            metaDescription = (string?)null,
        });

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var redirect = await db.Redirects.FirstOrDefaultAsync(r => r.FromPath == "/" + oldSlug);

        redirect.Should().NotBeNull();
        redirect!.ToPath.Should().Be("/" + newSlug);
        redirect.StatusCode.Should().Be(301);
    }

    [Fact]
    public async Task REQ_SITE_011_RendersTheSeoFieldsOnThePublicPage()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var slug = "seo-" + Guid.NewGuid().ToString("N")[..8];
        var id = await ContentFixture.CreateDraftPageAsync(editor, "Seo page", slug);

        await editor.PostAsync(new Uri($"/api/v1/admin/pages/{id}/publish", UriKind.Relative), null);

        using var anonymous = _fixture.CreateClient();
        var page = await anonymous.GetFromJsonAsync<PublicPageRow>($"/api/v1/public/pages/{slug}");

        page!.MetaTitle.Should().Be("Seo page");
        page.MetaDescription.Should().NotBeNullOrWhiteSpace();
        page.NoIndex.Should().BeFalse();
    }

    [Fact]
    public async Task REQ_SITE_003_Returns403_WhenSalesTriesToPublish()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var id = await ContentFixture.CreateDraftPageAsync(editor, "Sales cannot", "salescannot-" + Guid.NewGuid().ToString("N")[..8]);

        using var sales = await _fixture.ClientAsAsync(ContentFixture.SalesEmail);
        var response = await sales.PostAsync(new Uri($"/api/v1/admin/pages/{id}/publish", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "content is the Editor's job, not the Sales user's");
    }

    private sealed record VersionRow(int VersionNumber, DateTime CreatedAtUtc, string CreatedBy);

    private sealed record ScheduleRow(DateTime PublishAtUtc, DateTime? UnpublishAtUtc, string EditorTimeZone, DateTime PublishAtServerLocal);

    private sealed record BulkRow(Guid Id, string Title, string Outcome, string[] BlockingReferences);

    private sealed record PublicPageRow(string Title, string Slug, string? Body, string MetaTitle, string? MetaDescription, string? CanonicalUrl, bool NoIndex);
}
