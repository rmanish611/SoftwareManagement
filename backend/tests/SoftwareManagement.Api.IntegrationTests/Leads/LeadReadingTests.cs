using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Leads;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Leads;

/// <summary>
/// The other half of capture: reading back what arrived.
///
/// A lead written to a table nobody can read is not a captured lead. These tests come in through
/// the admin API with a token that carries the lead permission, so what they prove is the whole
/// path a person uses - not a row a test put there and read back with the same DbContext.
/// </summary>
[Collection(LeadTestGroup.Name)]
public sealed class LeadReadingTests(LeadFixture fixture)
{
    private readonly LeadFixture _fixture = fixture;

    [Fact]
    public async Task REQ_LEAD_001_The_enquiry_list_reads_back_what_the_public_form_wrote()
    {
        var nonce = LeadArrange.Nonce();
        using var visitor = _fixture.ClientFrom(LeadArrange.Address(nonce));

        (await visitor.PostAsJsonAsync("/api/v1/public/forms/contact/submit", LeadArrange.ContactBody(nonce)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var owner = await _fixture.ClientAsAsync(LeadFixture.OwnerEmail);
        var leads = await owner.GetFromJsonAsync<List<LeadArrange.LeadRow>>($"/api/v1/leads?search={nonce}");

        var mine = leads!.Should().ContainSingle(l => l.FullName == $"PROBE-{nonce}").Subject;
        mine.Stage.Should().Be(nameof(LeadStage.New));
        mine.Source.Should().Be(nameof(LeadSource.Direct));
        mine.Email.Should().Be($"probe-{nonce}@example.test");
    }

    [Fact]
    public async Task REQ_LEAD_001_The_enquiry_waiting_longest_is_the_first_one_the_owner_sees()
    {
        var nonce = LeadArrange.Nonce();

        // Two enquiries from the same sender, so one search finds both. The earlier one has the
        // earlier deadline.
        var older = await AddLeadAsync(nonce, LeadStage.New);
        var newer = await AddLeadAsync(nonce, LeadStage.New);

        await BackdateAsync(older, DateTime.UtcNow.AddDays(-3));

        var owner = await _fixture.ClientAsAsync(LeadFixture.OwnerEmail);
        var leads = await owner.GetFromJsonAsync<List<LeadArrange.LeadRow>>($"/api/v1/leads?search={nonce}");

        var olderAt = leads!.FindIndex(l => l.Id == older);
        var newerAt = leads.FindIndex(l => l.Id == newer);

        olderAt.Should().BeGreaterThanOrEqualTo(0);
        newerAt.Should().BeGreaterThanOrEqualTo(0);

        // This test asserted "newest first" when it was written in P07. P08 changed the ordering to
        // unanswered-first, oldest deadline before newest, because the enquiry the company is
        // currently failing is the one that belongs at the top - and the test was asserting the
        // opposite of the behaviour the inbox was built for.
        olderAt.Should().BeLessThan(newerAt);
    }

    [Fact]
    public async Task REQ_LEAD_001_Spam_is_left_out_of_the_default_view_and_found_only_when_asked_for_by_name()
    {
        var nonce = LeadArrange.Nonce();

        // Marked spam directly, the way the inbox will mark it in P08. Nothing in capture sets this
        // stage - a bot's submission is stored as spam without ever becoming a lead - so the filter
        // has to be arranged rather than provoked, and it still has to work today.
        var id = await AddLeadAsync(nonce, LeadStage.Spam);

        var owner = await _fixture.ClientAsAsync(LeadFixture.OwnerEmail);

        var byDefault = await owner.GetFromJsonAsync<List<LeadArrange.LeadRow>>($"/api/v1/leads?search={nonce}");
        byDefault!.Should().NotContain(l => l.Id == id);

        var asked = await owner.GetFromJsonAsync<List<LeadArrange.LeadRow>>("/api/v1/leads?stage=spam");
        asked!.Should().Contain(l => l.Id == id);
        asked.Should().OnlyContain(l => l.Stage == nameof(LeadStage.Spam));
    }

    [Fact]
    public async Task REQ_LEAD_001_A_stage_that_is_not_a_stage_shows_the_default_view_rather_than_failing()
    {
        var nonce = LeadArrange.Nonce();
        var spamId = await AddLeadAsync(nonce, LeadStage.Spam);
        var newId = await AddLeadAsync(LeadArrange.Nonce(), LeadStage.New);

        var owner = await _fixture.ClientAsAsync(LeadFixture.OwnerEmail);
        var response = await owner.GetAsync("/api/v1/leads?stage=not-a-stage");

        // A stale bookmark or a typed address must not be an error page, and must not quietly
        // widen the view to include spam either.
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var leads = await response.Content.ReadFromJsonAsync<List<LeadArrange.LeadRow>>();
        leads!.Should().Contain(l => l.Id == newId);
        leads.Should().NotContain(l => l.Id == spamId);
    }

    [Fact]
    public async Task REQ_LEAD_001_The_page_size_is_clamped_at_both_ends_however_it_is_asked_for()
    {
        var nonce = LeadArrange.Nonce();
        await AddLeadAsync(nonce, LeadStage.New);
        await AddLeadAsync(LeadArrange.Nonce(), LeadStage.New);

        var owner = await _fixture.ClientAsAsync(LeadFixture.OwnerEmail);

        // Zero and negative numbers become one, so a caller cannot ask for a page of nothing and
        // conclude there are no enquiries.
        (await owner.GetFromJsonAsync<List<LeadArrange.LeadRow>>("/api/v1/leads?pageSize=0"))!
            .Should().HaveCount(1);

        (await owner.GetFromJsonAsync<List<LeadArrange.LeadRow>>("/api/v1/leads?pageSize=-5"))!
            .Should().HaveCount(1);

        // And a caller asking for a hundred thousand gets the ceiling, not a table scan
        // (NFR-PERF-04).
        (await owner.GetFromJsonAsync<List<LeadArrange.LeadRow>>("/api/v1/leads?pageSize=100000"))!
            .Should().HaveCountLessThanOrEqualTo(100).And.HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task REQ_LEAD_006_One_enquiry_carries_the_exact_words_the_person_consented_to()
    {
        var nonce = LeadArrange.Nonce();
        using var visitor = _fixture.ClientFrom(LeadArrange.Address(nonce));

        (await visitor.PostAsJsonAsync("/api/v1/public/forms/contact/submit", LeadArrange.ContactBody(nonce)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var owner = await _fixture.ClientAsAsync(LeadFixture.OwnerEmail);
        var list = await owner.GetFromJsonAsync<List<LeadArrange.LeadRow>>($"/api/v1/leads?search={nonce}");
        var id = list!.Single(l => l.FullName == $"PROBE-{nonce}").Id;

        var detail = await owner.GetFromJsonAsync<LeadArrange.LeadDetailRow>($"/api/v1/leads/{id}");

        detail!.Message.Should().Be($"An enquiry about the system, reference {nonce}.");
        detail.Stage.Should().Be(nameof(LeadStage.New));
        detail.SlaDueAtUtc.Should().BeAfter(detail.CreatedAtUtc);

        // This is the answer a privacy request is served from: not "they ticked a box" but the
        // sentence that was on the screen, its version, and when (REQ-LEAD-006, NFR-PRIV-02).
        detail.Consent.Should().NotBeNull();
        detail.Consent!.Text.Should().NotBeEmpty();
        detail.Consent.Version.Should().BeGreaterThan(0);
        detail.Consent.Purpose.Should().NotBeEmpty();
        detail.Consent.GivenAtUtc.Should().BeCloseTo(detail.CreatedAtUtc, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task REQ_LEAD_003_The_detail_of_a_demo_request_names_the_product_it_came_from()
    {
        var nonce = LeadArrange.Nonce();
        var slug = await LeadArrange.PublishProductAsync(_fixture, nonce);

        using var visitor = _fixture.ClientFrom(LeadArrange.Address(nonce));
        var body = LeadArrange.ContactBody(nonce);
        body.Answers["product"] = slug;

        (await visitor.PostAsJsonAsync("/api/v1/public/forms/request-demo/submit", body))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var owner = await _fixture.ClientAsAsync(LeadFixture.OwnerEmail);
        var list = await owner.GetFromJsonAsync<List<LeadArrange.LeadRow>>($"/api/v1/leads?search={nonce}");
        var id = list!.Single(l => l.FullName == $"PROBE-{nonce}").Id;

        var detail = await owner.GetFromJsonAsync<LeadArrange.LeadDetailRow>($"/api/v1/leads/{id}");

        // The name, not the slug: whoever rings back reads this, and "probe-product-a1b2" is not a
        // thing anyone says out loud.
        detail!.Product.Should().Be($"Probe product {nonce}");
    }

    [Fact]
    public async Task REQ_LEAD_001_An_enquiry_that_does_not_exist_is_a_404_rather_than_an_empty_lead()
    {
        var owner = await _fixture.ClientAsAsync(LeadFixture.OwnerEmail);

        var response = await owner.GetAsync($"/api/v1/leads/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task REQ_LEAD_001_One_enquiry_is_as_closed_to_a_stranger_as_the_whole_list_is()
    {
        var nonce = LeadArrange.Nonce();
        var id = await AddLeadAsync(nonce, LeadStage.New);

        using var anonymous = _fixture.CreateClient();
        (await anonymous.GetAsync($"/api/v1/leads/{id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The editor maintains the site. One person's name, email and telephone number is not
        // theirs to read one at a time either (AZ-21).
        var editor = await _fixture.ClientAsAsync(LeadFixture.EditorEmail);
        (await editor.GetAsync($"/api/v1/leads/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);
        (await sales.GetAsync($"/api/v1/leads/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Writes one lead in a chosen stage, for the filters that capture itself cannot produce.
    /// </summary>
    private async Task<Guid> AddLeadAsync(string nonce, LeadStage stage)
    {
        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            FullName = $"PROBE-{nonce}",
            Email = $"probe-{nonce}@example.test",
            Message = $"Arranged for the reading tests, reference {nonce}.",
            Stage = stage,
            Source = LeadSource.Direct,
            CreatedAtUtc = now,
            SlaDueAtUtc = now.AddHours(9),
            CreatedBy = "test-fixture",
        };

        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id;
    }

    /// <summary>
    /// Moves a lead's arrival and its deadline into the past. The audit stamp overwrites
    /// CreatedAtUtc on insert, which is right for the application and wrong for a test that needs
    /// an enquiry from three days ago.
    /// </summary>
    private async Task BackdateAsync(Guid id, DateTime createdAtUtc)
    {
        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Leads.Where(l => l.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.CreatedAtUtc, createdAtUtc)
                .SetProperty(l => l.SlaDueAtUtc, createdAtUtc.AddHours(9)));
    }
}
