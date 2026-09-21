using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Portfolio;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Portfolio;

/// <summary>
/// The developer directory.
///
/// The reader here is somebody deciding whether to build against us, and every rule is about not
/// wasting their time: no entry without a version, exactly one version to build against, and no
/// withdrawal without a quarter's notice.
/// </summary>
[Collection(PortfolioTestGroup.Name)]
public sealed class ApiCatalogTests(PortfolioFixture fixture)
{
    private readonly PortfolioFixture _fixture = fixture;

    [Fact]
    public async Task REQ_API_001_An_entry_with_no_version_cannot_be_published()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, _) = await CreateAsync(editor, nonce);

        var published = await editor.PostAsync($"/api/v1/admin/api-catalog/{id}/publish", null);
        published.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await Problem(published);
        Code(problem).Should().Be("API_NOT_READY");
        problem.GetProperty("shortfalls").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("at least one version");
    }

    [Fact]
    public async Task REQ_API_001_Publishing_an_entry_leaves_exactly_one_version_flagged_current()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce);

        await AddVersionAsync(editor, id, "v1");
        await AddVersionAsync(editor, id, "v2");
        await PublishAsync(editor, id);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var versions = await db.ApiVersions.AsNoTracking().Where(v => v.ApiCatalogEntryId == id).ToListAsync();
        versions.Should().HaveCount(2);
        versions.Count(v => v.IsCurrent).Should().Be(1);

        // The first version added is the one to build against until somebody says otherwise.
        versions.Single(v => v.IsCurrent).VersionLabel.Should().Be("v1");

        var entries = await PublicAsync();
        entries.Should().ContainSingle(e => e.GetProperty("slug").GetString() == slug);
    }

    [Fact]
    public async Task REQ_API_002_Marking_a_version_current_clears_the_one_that_was()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, _) = await CreateAsync(editor, nonce);

        await AddVersionAsync(editor, id, "1.0");
        var beta = await AddVersionAsync(editor, id, "2.0", status: "Beta");

        (await editor.PostAsync($"/api/v1/admin/api-catalog/versions/{beta}/current", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var versions = await db.ApiVersions.AsNoTracking().Where(v => v.ApiCatalogEntryId == id).ToListAsync();

        // A filtered unique index stands behind this, so two current versions cannot exist even if
        // a future write path forgets to clear the old one.
        versions.Should().ContainSingle(v => v.IsCurrent);
        versions.Single(v => v.IsCurrent).VersionLabel.Should().Be("2.0");
    }

    [Fact]
    public async Task REQ_API_002_A_deprecated_version_cannot_be_made_the_one_to_build_against()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, _) = await CreateAsync(editor, nonce);

        var old = await AddVersionAsync(editor, id, "1.0");
        var replacement = await AddVersionAsync(editor, id, "2.0");

        await MakeCurrentAsync(editor, replacement);
        await DeprecateAsync(editor, old, DaysAway(150), HttpStatusCode.NoContent);

        var response = await editor.PostAsync($"/api/v1/admin/api-catalog/versions/{old}/current", null);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await Problem(response)).Should().Be("VERSION_DEPRECATED");
    }

    [Fact]
    public async Task REQ_API_003_A_sunset_thirty_days_away_is_refused()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, _) = await CreateAsync(editor, nonce);

        var old = await AddVersionAsync(editor, id, "1.0");
        var replacement = await AddVersionAsync(editor, id, "2.0");
        await MakeCurrentAsync(editor, replacement);

        var response = await DeprecateAsync(editor, old, DaysAway(30), HttpStatusCode.UnprocessableEntity);

        var problem = await Problem(response);
        Code(problem).Should().Be("SUNSET_TOO_SOON");

        // Both numbers are in the message, so the person does not have to work out what would be
        // acceptable (BR-API-03).
        problem.GetProperty("detail").GetString().Should().Contain("90");
    }

    [Fact]
    public async Task REQ_API_003_A_sunset_a_hundred_and_twenty_days_away_is_accepted_and_shown_publicly()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce);

        var old = await AddVersionAsync(editor, id, "1.0");
        var replacement = await AddVersionAsync(editor, id, "2.0");
        await MakeCurrentAsync(editor, replacement);

        var sunset = DaysAway(120);
        await DeprecateAsync(editor, old, sunset, HttpStatusCode.NoContent);
        await PublishAsync(editor, id);

        var entry = (await PublicAsync()).Single(e => e.GetProperty("slug").GetString() == slug);
        var deprecated = entry.GetProperty("versions").EnumerateArray()
            .Single(v => v.GetProperty("versionLabel").GetString() == "1.0");

        deprecated.GetProperty("status").GetString().Should().Be("Deprecated");
        deprecated.GetProperty("sunsetDate").GetString().Should().Be(sunset.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task REQ_API_003_Deprecating_the_current_version_is_refused_until_a_replacement_is_current()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, _) = await CreateAsync(editor, nonce);

        var only = await AddVersionAsync(editor, id, "1.0");

        // Deprecating the only current version would leave the directory telling developers to
        // build against nothing (EX-142).
        var response = await DeprecateAsync(editor, only, DaysAway(200), HttpStatusCode.Conflict);
        Code(await Problem(response)).Should().Be("NO_REPLACEMENT");
    }

    [Fact]
    public async Task REQ_API_004_The_public_entry_carries_the_base_url_auth_scheme_current_version_and_docs()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce, hasSandbox: true);

        await AddVersionAsync(editor, id, "v1");
        await PublishAsync(editor, id);

        var entry = (await PublicAsync()).Single(e => e.GetProperty("slug").GetString() == slug);

        entry.GetProperty("baseUrl").GetString().Should().Be("https://api.example.test/billing");
        entry.GetProperty("authScheme").GetString().Should().Be("OAuth2");
        entry.GetProperty("currentVersion").GetString().Should().Be("v1");
        entry.GetProperty("docsUrl").GetString().Should().Be("https://docs.example.test/billing");
        entry.GetProperty("purpose").GetString().Should().NotBeNullOrWhiteSpace();
        entry.GetProperty("hasSandbox").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task REQ_API_004_An_entry_without_a_sandbox_says_so_rather_than_offering_one()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce, hasSandbox: false);

        await AddVersionAsync(editor, id, "v1");
        await PublishAsync(editor, id);

        var entry = (await PublicAsync()).Single(e => e.GetProperty("slug").GetString() == slug);
        entry.GetProperty("hasSandbox").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task REQ_API_004_A_draft_entry_is_not_in_the_public_directory()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce);

        await AddVersionAsync(editor, id, "v1");

        (await PublicAsync()).Should().NotContain(e => e.GetProperty("slug").GetString() == slug);
    }

    [Fact]
    public async Task REQ_API_005_A_published_api_appears_on_the_page_of_the_product_it_belongs_to()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce, productId: _fixture.ProductId);

        await AddVersionAsync(editor, id, "v1");
        await PublishAsync(editor, id);

        var page = await ProductPageAsync();

        page.GetProperty("apis").EnumerateArray()
            .Select(a => a.GetProperty("slug").GetString())
            .Should().Contain(slug);
    }

    [Fact]
    public async Task REQ_API_005_An_unpublished_api_is_absent_from_the_product_page()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce, productId: _fixture.ProductId);

        await AddVersionAsync(editor, id, "v1");

        var page = await ProductPageAsync();

        page.GetProperty("apis").EnumerateArray()
            .Select(a => a.GetProperty("slug").GetString())
            .Should().NotContain(slug);
    }

    [Fact]
    public async Task REQ_API_006_A_version_with_a_changelog_shows_the_link()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce);

        await AddVersionAsync(editor, id, "v1", changelogUrl: "https://docs.example.test/billing/changelog");
        await PublishAsync(editor, id);

        var version = (await PublicAsync()).Single(e => e.GetProperty("slug").GetString() == slug)
            .GetProperty("versions").EnumerateArray().Single();

        version.GetProperty("changelogUrl").GetString().Should().Be("https://docs.example.test/billing/changelog");
    }

    [Fact]
    public async Task REQ_API_006_A_version_without_a_changelog_emits_no_dead_link()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce);

        await AddVersionAsync(editor, id, "v1");
        await PublishAsync(editor, id);

        var version = (await PublicAsync()).Single(e => e.GetProperty("slug").GetString() == slug)
            .GetProperty("versions").EnumerateArray().Single();

        // Null rather than an empty string: the page renders a link only when there is one to
        // render, and "" would become an anchor pointing at the current page.
        version.GetProperty("changelogUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task REQ_API_007_The_sitemap_lists_published_api_entries_and_omits_drafts()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);

        var (first, firstSlug) = await CreateAsync(editor, nonce + "a");
        await AddVersionAsync(editor, first, "v1");
        await PublishAsync(editor, first);

        var (second, secondSlug) = await CreateAsync(editor, nonce + "b");
        await AddVersionAsync(editor, second, "v1");
        await PublishAsync(editor, second);

        var (_, draftSlug) = await CreateAsync(editor, nonce + "c");

        var sitemap = await SitemapAsync();

        sitemap.Should().Contain($"<loc>/developers/{firstSlug}</loc>");
        sitemap.Should().Contain($"<loc>/developers/{secondSlug}</loc>");
        sitemap.Should().NotContain(draftSlug);
    }

    [Fact]
    public async Task REQ_API_007_The_sitemap_is_xml_and_carries_no_admin_route()
    {
        var response = await _fixture.CreateClient().GetAsync("/api/v1/public/sitemap.xml");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().StartWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        body.Should().Contain("<loc>/developers</loc>");
        body.Should().NotContain("/api/v1/admin");
    }

    [Fact]
    public async Task REQ_API_008_A_sunset_inside_the_window_is_reported_with_the_days_remaining()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, _) = await CreateAsync(editor, nonce);

        var old = await AddVersionAsync(editor, id, "1.0");
        var replacement = await AddVersionAsync(editor, id, "2.0");
        await MakeCurrentAsync(editor, replacement);
        await DeprecateAsync(editor, old, DaysAway(100), HttpStatusCode.NoContent);

        var report = await editor.GetFromJsonAsync<JsonElement>("/api/v1/admin/api-catalog/sunsets?withinDays=120");

        var row = report.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("apiName").GetString() == $"Billing API {nonce}");

        row.GetProperty("versionLabel").GetString().Should().Be("1.0");
        row.GetProperty("daysRemaining").GetInt32().Should().Be(100);
        report.GetProperty("emptyMessage").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task REQ_API_008_A_window_with_nothing_in_it_answers_in_words_rather_than_a_blank_table()
    {
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);

        // One day out: every deprecation these tests create gives at least ninety days' notice, so
        // this window is reliably empty however many have been written.
        var report = await editor.GetFromJsonAsync<JsonElement>("/api/v1/admin/api-catalog/sunsets?withinDays=1");

        report.GetProperty("rows").GetArrayLength().Should().Be(0);
        report.GetProperty("emptyMessage").GetString().Should().Contain("No API version");
        report.GetProperty("withinDays").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task REQ_API_008_An_auditor_may_read_the_sunset_report_and_an_editor_may_write_the_catalogue()
    {
        var sales = await _fixture.ClientAsAsync(PortfolioFixture.SalesEmail);

        // Sales holds the read permission on the catalogue and not the write one, which is the row
        // the authorization matrix draws for them.
        (await sales.GetAsync("/api/v1/admin/api-catalog/sunsets")).StatusCode.Should().Be(HttpStatusCode.OK);

        var written = await sales.PostAsJsonAsync("/api/v1/admin/api-catalog", new
        {
            name = "Not mine to write",
            purpose = "Nothing.",
            authScheme = "None",
        });

        written.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void REQ_API_003_The_notice_rule_is_counted_from_the_day_the_deprecation_is_applied()
    {
        var today = new DateOnly(2026, 9, 21);

        ApiVersion.HasEnoughNotice(today.AddDays(89), today).Should().BeFalse();
        ApiVersion.HasEnoughNotice(today.AddDays(90), today).Should().BeTrue();
        ApiVersion.HasEnoughNotice(null, today).Should().BeFalse();
    }

    private static string Nonce() => Guid.NewGuid().ToString("N")[..8];

    private static DateOnly DaysAway(int days) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(days);

    private static async Task<(Guid Id, string Slug)> CreateAsync(
        HttpClient editor, string nonce, bool hasSandbox = false, Guid? productId = null)
    {
        var slug = $"billing-api-{nonce}";

        var created = await editor.PostAsJsonAsync("/api/v1/admin/api-catalog", new
        {
            name = $"Billing API {nonce}",
            slug,
            purpose = "Raise invoices and record payments from your own systems.",
            baseUrl = "https://api.example.test/billing",
            authScheme = "OAuth2",
            docsUrl = "https://docs.example.test/billing",
            openApiUrl = "https://docs.example.test/billing/openapi.json",
            hasSandbox,
            productId,
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return ((await created.Content.ReadFromJsonAsync<CreatedEntry>())!.Id, slug);
    }

    private static async Task<Guid> AddVersionAsync(
        HttpClient editor, Guid entryId, string label, string? status = null, string? changelogUrl = null)
    {
        var created = await editor.PostAsJsonAsync($"/api/v1/admin/api-catalog/{entryId}/versions", new
        {
            versionLabel = label,
            status,
            releasedOn = "2026-01-01",
            changelogUrl,
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<CreatedVersion>())!.Id;
    }

    private static async Task MakeCurrentAsync(HttpClient editor, Guid versionId) =>
        (await editor.PostAsync($"/api/v1/admin/api-catalog/versions/{versionId}/current", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

    private static async Task<HttpResponseMessage> DeprecateAsync(
        HttpClient editor, Guid versionId, DateOnly sunset, HttpStatusCode expected)
    {
        var response = await editor.PostAsJsonAsync(
            $"/api/v1/admin/api-catalog/versions/{versionId}/deprecate",
            new { sunsetDate = sunset.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });

        response.StatusCode.Should().Be(expected);
        return response;
    }

    private static async Task PublishAsync(HttpClient editor, Guid entryId) =>
        (await editor.PostAsync($"/api/v1/admin/api-catalog/{entryId}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

    private async Task<List<JsonElement>> PublicAsync() =>
        [.. (await _fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/public/apis")).EnumerateArray()];

    private async Task<JsonElement> ProductPageAsync() =>
        await _fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/public/products/portfolio-fixture-product");

    private async Task<string> SitemapAsync() =>
        await _fixture.CreateClient().GetStringAsync("/api/v1/public/sitemap.xml");

    private static async Task<JsonElement> Problem(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string? Code(JsonElement problem) => problem.GetProperty("code").GetString();

    private sealed record CreatedEntry(Guid Id, string Name, string Slug);

    private sealed record CreatedVersion(Guid Id, string VersionLabel, bool IsCurrent);
}
