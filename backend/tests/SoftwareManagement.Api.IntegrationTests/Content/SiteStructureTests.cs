using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace SoftwareManagement.Api.IntegrationTests.Content;

/// <summary>
/// Sections, navigation, media, services, technologies, team and testimonials: the parts of the
/// site the owner maintains without a developer.
/// </summary>
[Trait("Category", "Integration")]
[Collection(ContentTestGroup.Name)]
public sealed class SiteStructureTests(ContentFixture fixture)
{
    private readonly ContentFixture _fixture = fixture;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    // A one-pixel PNG. Real bytes, so the magic-number check sees what it would see in production.
    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName, string declaredType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(declaredType);

        var form = new MultipartFormDataContent { { content, "file", fileName } };
        return form;
    }

    [Fact]
    public async Task REQ_SITE_002_KeepsSectionOrderContiguousAfterAReorder()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var pageId = await ContentFixture.CreateDraftPageAsync(editor, "Sectioned", Unique("sections"));

        var ids = new List<Guid>();
        foreach (var heading in new[] { "First", "Second", "Third" })
        {
            var added = await editor.PostAsJsonAsync($"/api/v1/admin/pages/{pageId}/sections",
                new { sectionType = "RichText", heading, body = heading, mediaAssetId = (Guid?)null });
            added.EnsureSuccessStatusCode();
            ids.Add((await added.Content.ReadFromJsonAsync<SectionRow>())!.Id);
        }

        var reordered = await editor.PutAsJsonAsync($"/api/v1/admin/pages/{pageId}/sections/order",
            new { sectionIds = new[] { ids[2], ids[0], ids[1] } });

        reordered.StatusCode.Should().Be(HttpStatusCode.OK);
        var sections = await reordered.Content.ReadFromJsonAsync<List<SectionRow>>();

        sections.Should().NotBeNull();
        sections!.Select(s => s.SortOrder).Should().Equal(1, 2, 3);
        sections[0].Heading.Should().Be("Third");
    }

    [Fact]
    public async Task REQ_SITE_002_Returns409_WhenTheReorderDoesNotListEverySectionOnce()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var pageId = await ContentFixture.CreateDraftPageAsync(editor, "Partial order", Unique("partial"));

        var added = await editor.PostAsJsonAsync($"/api/v1/admin/pages/{pageId}/sections",
            new { sectionType = "RichText", heading = "Only", body = "Only", mediaAssetId = (Guid?)null });
        var only = (await added.Content.ReadFromJsonAsync<SectionRow>())!.Id;

        var response = await editor.PutAsJsonAsync($"/api/v1/admin/pages/{pageId}/sections/order",
            new { sectionIds = new[] { only, only } });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task REQ_SITE_008_HidesAMenuItemWhoseTargetPageIsNotPublished()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var slug = Unique("hidden");
        var pageId = await ContentFixture.CreateDraftPageAsync(editor, "Hidden target", slug);

        var added = await editor.PostAsJsonAsync("/api/v1/admin/navigation",
            new { menu = "Header", label = "Hidden", pageId, externalUrl = (string?)null, opensInNewTab = false });
        added.EnsureSuccessStatusCode();

        using var anonymous = _fixture.CreateClient();
        var menu = await anonymous.GetFromJsonAsync<List<PublicNavRow>>("/api/v1/public/navigation");

        menu!.Should().NotContain(item => item.Label == "Hidden",
            "a menu item pointing at a draft would be a dead link");
    }

    [Fact]
    public async Task REQ_SITE_008_Returns409_WhenDeletingAPageAMenuStillLinksTo()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var pageId = await ContentFixture.CreateDraftPageAsync(editor, "Linked page", Unique("linked"));

        await editor.PostAsJsonAsync("/api/v1/admin/navigation",
            new { menu = "Footer", label = "Linked", pageId, externalUrl = (string?)null, opensInNewTab = false });

        using var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);
        var deleted = await owner.DeleteAsync(new Uri($"/api/v1/admin/pages/{pageId}", UriKind.Relative));

        deleted.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await deleted.Content.ReadAsStringAsync()).Should().Contain("Linked");
    }

    [Fact]
    public async Task REQ_SITE_008_Returns422_WhenAMenuItemHasBothOrNeitherDestination()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var neither = await editor.PostAsJsonAsync("/api/v1/admin/navigation",
            new { menu = "Header", label = "Nowhere", pageId = (Guid?)null, externalUrl = (string?)null, opensInNewTab = false });

        neither.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task REQ_SITE_009_StoresAnUploadedImageWithItsDimensionsAndChecksum()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        using var form = FileContent(TinyPng, "logo.png", "image/png");
        var response = await editor.PostAsync(new Uri("/api/v1/admin/media", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var asset = await response.Content.ReadFromJsonAsync<MediaRow>();

        asset!.ContentType.Should().Be("image/png");
        asset.Kind.Should().Be("Image");
        asset.SizeBytes.Should().Be(TinyPng.Length);
        asset.Width.Should().Be(1, "the web version is produced at upload time");
    }

    [Fact]
    public async Task REQ_SITE_009_Returns415_WhenTheBytesDoNotMatchTheClaimedType()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        // A Windows executable renamed to .png and declared as an image. The name and the declared
        // type are both attacker-controlled, so only the bytes are believed.
        byte[] executable = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00];

        using var form = FileContent(executable, "innocent.png", "image/png");
        var response = await editor.PostAsync(new Uri("/api/v1/admin/media", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await response.Content.ReadAsStringAsync()).Should().Contain("contents decide");
    }

    [Fact]
    public async Task REQ_SITE_009_Returns400_WhenNoFileIsSent()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        using var form = FileContent([], "empty.png", "image/png");
        var response = await editor.PostAsync(new Uri("/api/v1/admin/media", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task REQ_SITE_010_ListsAServiceWithTheTechnologiesItUses()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var technology = await editor.PostAsJsonAsync("/api/v1/admin/technologies",
            new { name = Unique("Tech"), category = "Framework", proficiency = (byte?)4 });
        technology.EnsureSuccessStatusCode();
        var techId = (await technology.Content.ReadFromJsonAsync<TechnologyRow>())!.Id;

        var service = await editor.PostAsJsonAsync("/api/v1/admin/services", new
        {
            name = Unique("Service"),
            summary = "We build and run it.",
            body = (string?)null,
            isPublished = true,
            technologyIds = new[] { techId },
        });

        service.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await service.Content.ReadFromJsonAsync<ServiceRow>();
        created!.Technologies.Should().HaveCount(1);

        using var anonymous = _fixture.CreateClient();
        var published = await anonymous.GetFromJsonAsync<List<ServiceRow>>("/api/v1/public/services");
        published!.Should().Contain(s => s.Name == created.Name);
    }

    [Fact]
    public async Task REQ_SITE_010_Returns409_WhenDeletingATechnologyAServiceStillLists()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var technology = await editor.PostAsJsonAsync("/api/v1/admin/technologies",
            new { name = Unique("InUse"), category = "Database", proficiency = (byte?)null });
        var techId = (await technology.Content.ReadFromJsonAsync<TechnologyRow>())!.Id;

        await editor.PostAsJsonAsync("/api/v1/admin/services", new
        {
            name = Unique("Uses"),
            summary = "Uses that technology.",
            body = (string?)null,
            isPublished = false,
            technologyIds = new[] { techId },
        });

        using var owner = await _fixture.ClientAsAsync(ContentFixture.OwnerEmail);
        var deleted = await owner.DeleteAsync(new Uri($"/api/v1/admin/technologies/{techId}", UriKind.Relative));

        deleted.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task REQ_SITE_012_RefusesToPublishATestimonialWithoutRecordedPermission()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);

        var response = await editor.PostAsJsonAsync("/api/v1/admin/site/testimonials", new
        {
            authorName = "A Customer",
            authorRole = "Operations Head",
            organisationName = "A Company",
            quote = "It replaced three spreadsheets.",
            hasPermission = false,
            isPublished = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("permission");
    }

    [Fact]
    public async Task REQ_SITE_012_ShowsOnlyPermittedTestimonialsOnThePublicSite()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var permitted = Unique("Permitted");

        await editor.PostAsJsonAsync("/api/v1/admin/site/testimonials", new
        {
            authorName = permitted,
            authorRole = "Director",
            organisationName = "Willing Ltd",
            quote = "Happy to be quoted.",
            hasPermission = true,
            isPublished = true,
        });

        await editor.PostAsJsonAsync("/api/v1/admin/site/testimonials", new
        {
            authorName = Unique("Unwilling"),
            authorRole = "Director",
            organisationName = "Private Ltd",
            quote = "Said this in confidence.",
            hasPermission = false,
            isPublished = false,
        });

        using var anonymous = _fixture.CreateClient();
        var company = await anonymous.GetFromJsonAsync<CompanyRow>("/api/v1/public/company");

        company!.Testimonials.Should().Contain(t => t.AuthorName == permitted);
        company.Testimonials.Should().NotContain(t => t.Quote.Contains("in confidence", StringComparison.Ordinal));
    }

    [Fact]
    public async Task REQ_SITE_012_Returns422_ForABannerThatEndsBeforeItStarts()
    {
        using var editor = await _fixture.ClientAsAsync(ContentFixture.EditorEmail);
        var start = DateTime.UtcNow.AddDays(1);

        var response = await editor.PostAsJsonAsync("/api/v1/admin/site/announcements", new
        {
            message = "Backwards banner",
            linkUrl = (string?)null,
            startsAtUtc = start,
            endsAtUtc = start.AddHours(-2),
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private sealed record SectionRow(Guid Id, string SectionType, string? Heading, string? Body, int SortOrder, bool IsVisible);

    private sealed record PublicNavRow(string Menu, string Label, string Href, bool OpensInNewTab);

    private sealed record MediaRow(Guid Id, string FileName, string ContentType, long SizeBytes, int? Width, int? Height, string? AltText, string Kind);

    private sealed record TechnologyRow(Guid Id, string Name, string Category, byte? Proficiency);

    private sealed record ServiceRow(Guid Id, string Name, string Slug, string Summary, bool IsPublished, string[] Technologies);

    private sealed record TestimonialRow(string Quote, string AuthorName, string AuthorRole, string? OrganisationName);

    private sealed record CompanyRow(string Name, string City, object[] Team, TestimonialRow[] Testimonials, object? Announcement);
}
