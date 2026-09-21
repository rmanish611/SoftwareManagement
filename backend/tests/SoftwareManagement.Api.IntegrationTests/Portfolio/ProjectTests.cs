using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Portfolio;

/// <summary>
/// Delivered work: what may be said about it, and about whom.
///
/// Nearly every rule here is a promise to somebody outside the company - a client who agreed to be
/// named or did not, a person whose words are being quoted, a buyer being told a number. The tests
/// are written from that side rather than from the shape of the form.
/// </summary>
[Collection(PortfolioTestGroup.Name)]
public sealed class ProjectTests(PortfolioFixture fixture)
{
    private readonly PortfolioFixture _fixture = fixture;

    [Fact]
    public async Task REQ_PRJ_001_A_published_project_reaches_the_portfolio_and_the_database()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce, Industry(nonce));

        (await editor.PostAsync($"/api/v1/admin/projects/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == id);
        row.Title.Should().Be($"PROBE-{nonce}");
        row.Status.Should().Be(ContentStatus.Published);
        row.PublishedAtUtc.Should().NotBeNull();

        var cards = await ListAsync(Industry(nonce));
        cards.Should().ContainSingle().Which.Slug.Should().Be(slug);
    }

    [Fact]
    public async Task REQ_PRJ_001_A_project_that_finished_before_it_started_is_refused()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);

        var response = await editor.PostAsJsonAsync("/api/v1/admin/projects", new
        {
            title = $"Backwards {nonce}",
            slug = $"backwards-{nonce}",
            industry = Industry(nonce),
            summary = "A project with its dates the wrong way round.",
            startedOn = "2026-06-01",
            completedOn = "2026-05-01",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(response)).Should().Be("DATES_REVERSED");
    }

    [Fact]
    public async Task REQ_PRJ_001_A_draft_project_is_not_on_the_public_list()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        await CreateAsync(editor, nonce, Industry(nonce));

        // No publish call. The portfolio is a claim about finished work, and a draft is a claim
        // nobody has approved yet.
        (await ListAsync(Industry(nonce))).Should().BeEmpty();
    }

    [Fact]
    public async Task REQ_PRJ_001_A_salesperson_may_read_projects_but_not_write_one()
    {
        var sales = await _fixture.ClientAsAsync(PortfolioFixture.SalesEmail);

        (await sales.GetAsync("/api/v1/admin/projects")).StatusCode.Should().Be(HttpStatusCode.OK);

        var written = await sales.PostAsJsonAsync("/api/v1/admin/projects", new
        {
            title = "Not mine to write",
            industry = "Retail",
            startedOn = "2026-01-01",
        });

        written.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task REQ_PRJ_002_A_case_study_with_no_outcome_metric_is_refused()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, _) = await CreateAsync(editor, nonce, Industry(nonce));

        await SetCaseStudyAsync(editor, id, metrics: []);

        var published = await editor.PostAsync($"/api/v1/admin/projects/{id}/publish", null);
        published.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await Problem(published);
        Code(problem).Should().Be("CASE_STUDY_NOT_READY");

        // The refusal itemises what is short, so the editor goes back to the right box rather than
        // reading a sentence and guessing.
        problem.GetProperty("shortfalls").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("at least one outcome metric with a number and a unit");
    }

    [Fact]
    public async Task REQ_PRJ_002_A_metric_with_a_number_and_no_unit_is_refused()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, _) = await CreateAsync(editor, nonce, Industry(nonce));

        // "Reduced by 40" is a claim. "Reduced by 40 percent" is a measurement (BR-PRJ-02).
        await SetCaseStudyAsync(editor, id, metrics: [new { label = "Response time", value = 40m, unit = "" }]);

        var published = await editor.PostAsync($"/api/v1/admin/projects/{id}/publish", null);
        published.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        Code(await Problem(published)).Should().Be("CASE_STUDY_NOT_READY");
    }

    [Fact]
    public async Task REQ_PRJ_002_A_complete_case_study_renders_all_three_sections_and_the_metric()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce, Industry(nonce));

        await SetCaseStudyAsync(editor, id, metrics: [new { label = "Response time", value = 40m, unit = "percent faster" }]);
        await PublishAsync(editor, id);

        var page = await PageAsync(slug);

        page.GetProperty("problem").GetString().Should().NotBeNullOrWhiteSpace();
        page.GetProperty("approach").GetString().Should().NotBeNullOrWhiteSpace();
        page.GetProperty("outcome").GetString().Should().NotBeNullOrWhiteSpace();

        var metric = page.GetProperty("metrics").EnumerateArray().Single();
        metric.GetProperty("label").GetString().Should().Be("Response time");
        metric.GetProperty("value").GetDecimal().Should().Be(40m);
        metric.GetProperty("unit").GetString().Should().Be("percent faster");
    }

    [Fact]
    public async Task REQ_PRJ_003_A_client_who_has_not_agreed_is_shown_as_the_industry_instead()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var logoId = await _fixture.AddLogoAsync($"Northwind Hospitals {nonce}", hasPermission: false);
        var (id, slug) = await CreateAsync(editor, nonce, "Healthcare", logoId: logoId);

        await PublishAsync(editor, id);

        // The anonymised label is not a fallback for missing data. It is the answer the client
        // agreed to, and it is computed on the server so no page can get it wrong (BR-PRJ-01).
        var page = await PageAsync(slug);
        page.GetProperty("client").GetString().Should().Be("a leading healthcare company");
        page.GetProperty("client").GetString().Should().NotContain("Northwind");
    }

    [Fact]
    public async Task REQ_PRJ_003_Permission_granted_later_puts_the_name_back_on_the_page()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var logoId = await _fixture.AddLogoAsync($"Northwind Hospitals {nonce}", hasPermission: false);
        var (id, slug) = await CreateAsync(editor, nonce, "Healthcare", logoId: logoId);

        await PublishAsync(editor, id);
        (await PageAsync(slug)).GetProperty("client").GetString().Should().StartWith("a leading");

        using (var scope = _fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var logo = await db.ClientLogos.SingleAsync(l => l.Id == logoId);
            logo.HasPermission = true;
            await db.SaveChangesAsync();
        }

        // Permission is a column, not a build-time decision: the page follows it on the next
        // request in both directions.
        (await PageAsync(slug)).GetProperty("client").GetString().Should().Be($"Northwind Hospitals {nonce}");
    }

    [Fact]
    public async Task REQ_PRJ_003_Naming_a_client_whose_logo_says_no_is_refused_at_publishing()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var logoId = await _fixture.AddLogoAsync($"Northwind Hospitals {nonce}", hasPermission: false);

        var created = await editor.PostAsJsonAsync("/api/v1/admin/projects", new
        {
            title = $"Named anyway {nonce}",
            slug = $"named-anyway-{nonce}",
            clientDisplayName = $"Northwind Hospitals {nonce}",
            industry = "Healthcare",
            summary = "A project whose editor typed the client name into the free-text field.",
            startedOn = "2026-01-01",
            clientLogoId = logoId,
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<CreatedProject>())!.Id;

        var published = await editor.PostAsync($"/api/v1/admin/projects/{id}/publish", null);
        published.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await Problem(published)).Should().Be("CLIENT_NOT_PERMITTED");
    }

    [Fact]
    public async Task REQ_PRJ_004_Filtering_by_industry_returns_only_that_industry()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);

        var mine = Industry(nonce);
        var theirs = Industry(nonce + "b");

        var (first, firstSlug) = await CreateAsync(editor, nonce + "-a", mine);
        var (second, _) = await CreateAsync(editor, nonce + "-b", theirs);

        await PublishAsync(editor, first);
        await PublishAsync(editor, second);

        var cards = await ListAsync(mine);
        cards.Should().ContainSingle().Which.Slug.Should().Be(firstSlug);
    }

    [Fact]
    public async Task REQ_PRJ_004_An_industry_with_nothing_published_answers_with_an_empty_list()
    {
        // An empty answer, not a 404: the page has an empty state with a contact link to render,
        // and a 404 would send the visitor to an error page instead.
        var response = await _fixture.CreateClient().GetAsync("/api/v1/public/projects?industry=Speleology");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await response.Content.ReadFromJsonAsync<List<ProjectCard>>())!.Should().BeEmpty();
    }

    [Fact]
    public async Task REQ_PRJ_004_The_industry_list_names_only_industries_with_published_work()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var published = Industry(nonce + "p");
        var draft = Industry(nonce + "d");

        var (id, _) = await CreateAsync(editor, nonce + "-p", published);
        await CreateAsync(editor, nonce + "-d", draft);
        await PublishAsync(editor, id);

        var industries = await _fixture.CreateClient()
            .GetFromJsonAsync<List<string>>("/api/v1/public/projects/industries");

        industries!.Should().Contain(published);
        industries.Should().NotContain(draft);
    }

    [Fact]
    public async Task REQ_PRJ_005_A_published_project_appears_on_the_page_of_the_product_it_used()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce, Industry(nonce), productId: _fixture.ProductId);

        await SetCaseStudyAsync(editor, id, metrics: [new { label = "Cost", value = 22m, unit = "percent lower" }]);
        await PublishAsync(editor, id);

        var page = await ProductPageAsync();

        var listed = page.GetProperty("projects").EnumerateArray()
            .Where(p => p.GetProperty("slug").GetString() == slug)
            .ToList();

        listed.Should().ContainSingle();
        listed[0].GetProperty("hasCaseStudy").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task REQ_PRJ_005_An_unpublished_project_is_absent_from_the_product_page()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (_, slug) = await CreateAsync(editor, nonce, Industry(nonce), productId: _fixture.ProductId);

        var page = await ProductPageAsync();

        page.GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("slug").GetString())
            .Should().NotContain(slug);
    }

    [Fact]
    public async Task REQ_PRJ_006_A_testimonial_whose_author_has_not_agreed_stops_publishing()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, _) = await CreateAsync(editor, nonce, Industry(nonce));
        var testimonialId = await _fixture.AddTestimonialAsync(nonce, hasPermission: false);

        await SetCaseStudyAsync(
            editor, id,
            metrics: [new { label = "Onboarding", value = 3m, unit = "days saved" }],
            testimonialId: testimonialId);

        var published = await editor.PostAsync($"/api/v1/admin/projects/{id}/publish", null);

        // Refused rather than quietly dropped from the page: the editor who attached the quote is
        // the person who can go and ask (BR-PRJ-03).
        published.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await Problem(published)).Should().Be("TESTIMONIAL_NOT_PERMITTED");
    }

    [Fact]
    public async Task REQ_PRJ_006_A_permitted_testimonial_renders_with_its_attribution()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce, Industry(nonce));
        var testimonialId = await _fixture.AddTestimonialAsync(nonce, hasPermission: true);

        await SetCaseStudyAsync(
            editor, id,
            metrics: [new { label = "Onboarding", value = 3m, unit = "days saved" }],
            testimonialId: testimonialId);

        await PublishAsync(editor, id);

        var quote = (await PageAsync(slug)).GetProperty("testimonial");
        quote.ValueKind.Should().NotBe(JsonValueKind.Null);
        quote.GetProperty("authorName").GetString().Should().Be($"Priya Sharma {nonce}");
        quote.GetProperty("authorRole").GetString().Should().Be("Operations Director");
        quote.GetProperty("quote").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task REQ_PRJ_006_An_unattributed_quote_is_refused_rather_than_published_anonymously()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, _) = await CreateAsync(editor, nonce, Industry(nonce));
        var testimonialId = await _fixture.AddTestimonialAsync(nonce, hasPermission: true, role: "   ");

        await SetCaseStudyAsync(
            editor, id,
            metrics: [new { label = "Onboarding", value = 3m, unit = "days saved" }],
            testimonialId: testimonialId);

        var published = await editor.PostAsync($"/api/v1/admin/projects/{id}/publish", null);
        published.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await Problem(published)).Should().Be("TESTIMONIAL_ANONYMOUS");
    }

    [Fact]
    public async Task REQ_PRJ_007_The_logo_wall_shows_permitted_logos_in_sort_order()
    {
        var nonce = Nonce();

        await _fixture.AddLogoAsync($"Wall D {nonce}", hasPermission: true, sortOrder: 940);
        await _fixture.AddLogoAsync($"Wall B {nonce}", hasPermission: true, sortOrder: 920);
        await _fixture.AddLogoAsync($"Wall A {nonce}", hasPermission: true, sortOrder: 910);
        await _fixture.AddLogoAsync($"Wall C {nonce}", hasPermission: true, sortOrder: 930);

        var wall = await _fixture.CreateClient().GetFromJsonAsync<List<LogoRow>>("/api/v1/public/client-logos");

        var mine = wall!.Where(l => l.Name.EndsWith(nonce, StringComparison.Ordinal)).Select(l => l.Name).ToList();
        mine.Should().Equal($"Wall A {nonce}", $"Wall B {nonce}", $"Wall C {nonce}", $"Wall D {nonce}");
        wall.Should().AllSatisfy(l => l.ImageUrl.Should().StartWith("/api/v1/public/media/"));
    }

    [Fact]
    public async Task REQ_PRJ_007_A_logo_whose_permission_is_withdrawn_disappears_on_the_next_request()
    {
        var nonce = Nonce();
        var logoId = await _fixture.AddLogoAsync($"Withdrawn {nonce}", hasPermission: true);

        var before = await _fixture.CreateClient().GetFromJsonAsync<List<LogoRow>>("/api/v1/public/client-logos");
        before!.Select(l => l.Name).Should().Contain($"Withdrawn {nonce}");

        using (var scope = _fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var logo = await db.ClientLogos.SingleAsync(l => l.Id == logoId);
            logo.HasPermission = false;
            await db.SaveChangesAsync();
        }

        var after = await _fixture.CreateClient().GetFromJsonAsync<List<LogoRow>>("/api/v1/public/client-logos");
        after!.Select(l => l.Name).Should().NotContain($"Withdrawn {nonce}");
    }

    [Fact]
    public async Task REQ_PRJ_008_A_project_lists_under_each_technology_it_was_built_with()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce, Industry(nonce));

        var technologies = new[]
        {
            await _fixture.AddTechnologyAsync($"Tech A {nonce}"),
            await _fixture.AddTechnologyAsync($"Tech B {nonce}"),
            await _fixture.AddTechnologyAsync($"Tech C {nonce}"),
        };

        (await editor.PutAsJsonAsync($"/api/v1/admin/projects/{id}/technologies", new { technologyIds = technologies }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await PublishAsync(editor, id);

        var usage = await UsageAsync();

        foreach (var letter in new[] { "A", "B", "C" })
        {
            var row = usage.Single(u => u.Name == $"Tech {letter} {nonce}");
            row.ProjectCount.Should().Be(1);
            row.Projects.Should().ContainSingle().Which.Slug.Should().Be(slug);
        }
    }

    [Fact]
    public async Task REQ_PRJ_008_A_technology_nobody_has_shipped_with_is_still_listed_with_a_zero()
    {
        var nonce = Nonce();
        await _fixture.AddTechnologyAsync($"Unused {nonce}");

        // Dropping it would make this page quietly disagree with the stack page beside it.
        var row = (await UsageAsync()).Single(u => u.Name == $"Unused {nonce}");
        row.ProjectCount.Should().Be(0);
        row.Projects.Should().BeEmpty();
    }

    [Fact]
    public async Task REQ_PRJ_008_A_technology_that_is_not_in_the_stack_list_is_refused()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, _) = await CreateAsync(editor, nonce, Industry(nonce));

        var response = await editor.PutAsJsonAsync(
            $"/api/v1/admin/projects/{id}/technologies", new { technologyIds = new[] { Guid.NewGuid() } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await Problem(response)).Should().Be("UNKNOWN_TECHNOLOGY");
    }

    [Fact]
    public async Task REQ_PRJ_009_An_unpublished_case_study_address_is_a_404_to_a_stranger()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce, Industry(nonce));

        await SetCaseStudyAsync(editor, id, metrics: [new { label = "Uptime", value = 99.9m, unit = "percent" }]);

        // A 403 would confirm it exists. A stranger gets the same answer for a draft as for an
        // address nobody ever used (NFR-AUTHZ-04).
        (await _fixture.CreateClient().GetAsync($"/api/v1/public/projects/{slug}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task REQ_PRJ_009_A_published_case_study_carries_its_whole_narrative_in_one_response()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);
        var (id, slug) = await CreateAsync(editor, nonce, Industry(nonce), productId: _fixture.ProductId);

        await SetCaseStudyAsync(editor, id, metrics: [new { label = "Uptime", value = 99.9m, unit = "percent" }]);
        await PublishAsync(editor, id);

        // The page has to stand alone for somebody arriving from a search result, so one request
        // carries the narrative, the metrics, the client label and the product it used.
        var page = await PageAsync(slug);
        page.GetProperty("title").GetString().Should().Be($"PROBE-{nonce}");
        page.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
        page.GetProperty("problem").GetString().Should().NotBeNullOrWhiteSpace();
        page.GetProperty("metrics").GetArrayLength().Should().Be(1);
        page.GetProperty("client").GetString().Should().NotBeNullOrWhiteSpace();
        page.GetProperty("product").GetString().Should().Be("Portfolio fixture product");
    }

    [Fact]
    public async Task REQ_PRJ_007_An_editor_records_a_logo_and_the_permission_separately()
    {
        var nonce = Nonce();
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);

        var created = await editor.PostAsJsonAsync("/api/v1/admin/client-logos", new
        {
            displayName = $"Recorded {nonce}",
            mediaAssetId = _fixture.LogoAssetId,
            hasPermission = false,
            sortOrder = 5,
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<LogoCreated>())!.Id;

        // Recorded without permission, so it is not on the wall yet.
        var wall = await _fixture.CreateClient().GetFromJsonAsync<List<LogoRow>>("/api/v1/public/client-logos");
        wall!.Select(l => l.Name).Should().NotContain($"Recorded {nonce}");

        (await editor.PutAsJsonAsync($"/api/v1/admin/client-logos/{id}/permission", new { hasPermission = true }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await _fixture.CreateClient().GetFromJsonAsync<List<LogoRow>>("/api/v1/public/client-logos");
        after!.Select(l => l.Name).Should().Contain($"Recorded {nonce}");
    }

    [Fact]
    public async Task REQ_PRJ_003_A_logo_pointing_at_no_image_is_refused()
    {
        var editor = await _fixture.ClientAsAsync(PortfolioFixture.EditorEmail);

        var response = await editor.PostAsJsonAsync("/api/v1/admin/client-logos", new
        {
            displayName = "Nothing to show",
            mediaAssetId = Guid.NewGuid(),
            hasPermission = true,
            sortOrder = 1,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await Problem(response)).Should().Be("UNKNOWN_ASSET");
    }

    private static string Nonce() => Guid.NewGuid().ToString("N")[..8];

    private static string Industry(string nonce) => "Industry-" + nonce;

    private static async Task<(Guid Id, string Slug)> CreateAsync(
        HttpClient editor, string nonce, string industry, Guid? logoId = null, Guid? productId = null)
    {
        var slug = $"probe-{nonce}";

        var created = await editor.PostAsJsonAsync("/api/v1/admin/projects", new
        {
            title = $"PROBE-{nonce}",
            slug,
            industry,
            summary = $"A delivered project written for test {nonce}.",
            startedOn = "2026-01-01",
            completedOn = "2026-04-01",
            clientLogoId = logoId,
            productId,
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return ((await created.Content.ReadFromJsonAsync<CreatedProject>())!.Id, slug);
    }

    private static async Task SetCaseStudyAsync(
        HttpClient editor, Guid id, object[] metrics, Guid? testimonialId = null)
    {
        var response = await editor.PutAsJsonAsync($"/api/v1/admin/projects/{id}/case-study", new
        {
            problem = "Three systems that did not talk to each other.",
            approach = "One schema, one import, and a month of double-running.",
            outcome = "The month-end close went from nine days to two.",
            metrics,
            testimonialId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static async Task PublishAsync(HttpClient editor, Guid id) =>
        (await editor.PostAsync($"/api/v1/admin/projects/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

    private async Task<List<ProjectCard>> ListAsync(string industry) =>
        (await _fixture.CreateClient()
            .GetFromJsonAsync<List<ProjectCard>>($"/api/v1/public/projects?industry={industry}"))!;

    private async Task<JsonElement> PageAsync(string slug) =>
        await _fixture.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/public/projects/{slug}");

    private async Task<JsonElement> ProductPageAsync() =>
        await _fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/public/products/portfolio-fixture-product");

    private async Task<List<UsageRow>> UsageAsync() =>
        (await _fixture.CreateClient().GetFromJsonAsync<List<UsageRow>>("/api/v1/public/technologies/usage"))!;

    private static async Task<JsonElement> Problem(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string? Code(JsonElement problem) => problem.GetProperty("code").GetString();

    private static async Task<string?> CodeOf(HttpResponseMessage response) => Code(await Problem(response));

    private sealed record CreatedProject(Guid Id, string Title, string Slug);

    private sealed record ProjectCard(string Title, string Slug, string Industry, string Client);

    private sealed record LogoRow(string Name, string ImageUrl, string AltText);

    private sealed record LogoCreated(Guid Id, string DisplayName, bool HasPermission);

    private sealed record UsageRow(string Name, string Category, int ProjectCount, IReadOnlyList<UsedProject> Projects);

    private sealed record UsedProject(string Title, string Slug);
}
