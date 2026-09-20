using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Application.Notifications;
using SoftwareManagement.Domain.Leads;
using SoftwareManagement.Domain.Notifications;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Leads;

/// <summary>
/// What happens when a stranger presses Send.
///
/// Every test here starts from its own apparent address and its own captcha token, because the
/// rules under test are about counting and about tokens being spendable once.
/// </summary>
[Collection(LeadTestGroup.Name)]
public sealed class LeadCaptureTests(LeadFixture fixture)
{
    private readonly LeadFixture _fixture = fixture;

    [Fact]
    public async Task REQ_LEAD_001_An_accepted_enquiry_writes_a_submission_a_consent_record_and_a_lead()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var response = await client.PostAsJsonAsync(
            "/api/v1/public/forms/contact/submit",
            LeadArrange.ContactBody(nonce));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var accepted = await response.Content.ReadFromJsonAsync<LeadArrange.SubmitRow>();
        accepted!.Reference.Should().StartWith("ENQ-");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.FullName == $"PROBE-{nonce}");
        lead.Stage.Should().Be(LeadStage.New);
        lead.Email.Should().Be($"probe-{nonce}@example.test");
        lead.SlaDueAtUtc.Should().BeAfter(lead.CreatedAtUtc);

        var submission = await db.FormSubmissions.AsNoTracking().SingleAsync(s => s.LeadId == lead.Id);
        submission.IsSpam.Should().BeFalse();
        submission.CaptchaOutcome.Should().Be(CaptchaOutcome.Passed);

        var consent = await db.ConsentRecords.AsNoTracking().SingleAsync(c => c.FormSubmissionId == submission.Id);
        consent.ConsentText.Should().NotBeEmpty();
        consent.ConsentVersion.Should().BeGreaterThan(0);
        consent.IpAddress.Should().Be(LeadArrange.Address(nonce));
    }

    [Fact]
    public async Task REQ_LEAD_001_An_enquiry_with_neither_an_email_nor_a_phone_number_is_refused()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var response = await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", new
        {
            answers = new Dictionary<string, string?> { ["fullName"] = $"PROBE-{nonce}", ["message"] = "No way to reply." },
            consent = true,
            captchaToken = LeadArrange.Token(nonce),
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<LeadArrange.ProblemRow>();
        problem!.Detail.Should().Contain("email address or a phone number");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Leads.AnyAsync(l => l.FullName == $"PROBE-{nonce}")).Should().BeFalse();
    }

    [Fact]
    public async Task REQ_LEAD_002_A_form_reports_exactly_the_fields_it_defines_with_their_required_flags()
    {
        using var client = _fixture.CreateClient();
        var form = await client.GetFromJsonAsync<LeadArrange.FormRow>("/api/v1/public/forms/request-demo");

        form!.Fields.Should().NotBeEmpty();
        form.Fields.Should().Contain(f => f.Name == "product" && f.FieldType == "ProductPicker");
        form.Fields.Single(f => f.Name == "email").IsRequired.Should().BeTrue();
        form.Fields.Single(f => f.Name == "phone").IsRequired.Should().BeFalse();
        form.ConsentText.Should().NotBeEmpty();

        // The hidden field is named by the server, so the client cannot get it wrong and a bot
        // cannot learn it from the markup alone.
        form.HoneypotField.Should().NotBeEmpty();
    }

    [Fact]
    public async Task REQ_LEAD_002_A_submission_missing_a_field_the_definition_marks_required_names_that_field()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var response = await client.PostAsJsonAsync("/api/v1/public/forms/request-demo/submit", new
        {
            answers = new Dictionary<string, string?> { ["phone"] = "9876543210" },
            consent = true,
            captchaToken = LeadArrange.Token(nonce),
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<LeadArrange.ProblemRow>();
        problem!.Field.Should().Be("fullName");
        problem.Detail.Should().Contain("Your name");
    }

    [Fact]
    public async Task REQ_LEAD_003_A_demo_request_from_a_product_page_puts_that_product_on_the_lead()
    {
        var nonce = LeadArrange.Nonce();
        var slug = await LeadArrange.PublishProductAsync(_fixture, nonce);

        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var body = LeadArrange.ContactBody(nonce);
        body.Answers["product"] = slug;

        var response = await client.PostAsJsonAsync("/api/v1/public/forms/request-demo/submit", body);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lead = await db.Leads.AsNoTracking().Include(l => l.Product).SingleAsync(l => l.FullName == $"PROBE-{nonce}");

        lead.ProductId.Should().NotBeNull();
        lead.Product!.Slug.Should().Be(slug);
    }

    [Fact]
    public async Task REQ_LEAD_003_A_quote_request_without_a_product_is_refused()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var body = LeadArrange.ContactBody(nonce);
        body.Answers["companyName"] = "A real organisation";

        var response = await client.PostAsJsonAsync("/api/v1/public/forms/request-quote/submit", body);

        // A quote for nothing in particular is not a quote (REQ-LEAD-003).
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<LeadArrange.ProblemRow>())!.Field.Should().Be("product");
    }

    [Fact]
    public async Task REQ_LEAD_004_A_captcha_token_presented_twice_is_refused_and_creates_no_lead()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var first = await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", LeadArrange.ContactBody(nonce));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // A different message, so the duplicate rule cannot be what refuses it: the only thing
        // reused is the token.
        var replay = LeadArrange.ContactBody(nonce);
        replay.Answers["message"] = "A completely different enquiry.";
        replay.Answers["email"] = $"other-{nonce}@example.test";

        var second = await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", replay);

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await second.Content.ReadFromJsonAsync<LeadArrange.ProblemRow>())!.Code.Should().Be("CAPTCHA_INVALID");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Leads.CountAsync(l => l.FullName == $"PROBE-{nonce}")).Should().Be(1);
    }

    [Fact]
    public async Task REQ_LEAD_004_A_submission_with_no_captcha_token_at_all_is_refused()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var body = LeadArrange.ContactBody(nonce);
        body.CaptchaToken = null;

        var response = await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<LeadArrange.ProblemRow>())!.Code.Should().Be("CAPTCHA_INVALID");
    }

    [Fact]
    public async Task REQ_LEAD_005_The_sixth_submission_from_one_address_in_ten_minutes_is_refused_with_a_retry_after()
    {
        var nonce = LeadArrange.Nonce();
        var address = LeadArrange.Address(nonce);
        using var client = _fixture.ClientFrom(address);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var body = LeadArrange.ContactBody($"{nonce}-{attempt}");
            var allowed = await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", body);
            allowed.StatusCode.Should().Be(HttpStatusCode.Accepted, $"submission {attempt} is inside the allowance");
        }

        var refused = await client.PostAsJsonAsync(
            "/api/v1/public/forms/contact/submit",
            LeadArrange.ContactBody($"{nonce}-6"));

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Without Retry-After the client has to guess, and it guesses in the direction that makes
        // the flood worse.
        refused.Headers.RetryAfter.Should().NotBeNull();
        (await refused.Content.ReadFromJsonAsync<LeadArrange.ProblemRow>())!.Code.Should().Be("RATE_LIMITED");
    }

    [Fact]
    public async Task REQ_LEAD_005_A_filled_honeypot_looks_accepted_stores_spam_and_queues_nothing()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var body = LeadArrange.ContactBody(nonce);
        body.Honeypot = "http://spam.example";

        var response = await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", body);

        // The bot is told nothing. An error here would teach the next attempt which field to leave
        // alone (BR-LEAD-03).
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Leads.AnyAsync(l => l.FullName == $"PROBE-{nonce}")).Should().BeFalse();

        var submission = await db.FormSubmissions.AsNoTracking()
            .SingleAsync(s => s.IpAddress == LeadArrange.Address(nonce));

        submission.IsSpam.Should().BeTrue();
        submission.LeadId.Should().BeNull();

        (await db.OutboxEmails.CountAsync(o => o.RelatedEntityId == submission.Id)).Should().Be(0);
    }

    [Fact]
    public async Task REQ_LEAD_006_A_submission_without_consent_is_refused_and_stores_nothing_at_all()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var body = LeadArrange.ContactBody(nonce);
        body.Consent = false;

        var response = await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<LeadArrange.ProblemRow>())!.Code.Should().Be("CONSENT_REQUIRED");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Keeping a record of someone who declined is precisely what the consent rule forbids.
        (await db.FormSubmissions.AnyAsync(s => s.IpAddress == LeadArrange.Address(nonce))).Should().BeFalse();
        (await db.Leads.AnyAsync(l => l.FullName == $"PROBE-{nonce}")).Should().BeFalse();
    }

    [Fact]
    public async Task REQ_LEAD_006_Consent_is_stored_with_the_exact_words_the_person_agreed_to()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        (await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", LeadArrange.ContactBody(nonce)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var form = await db.FormDefinitions.AsNoTracking().SingleAsync(f => f.Key == FormDefinition.Contact);
        var submission = await db.FormSubmissions.AsNoTracking()
            .Include(s => s.Consent)
            .SingleAsync(s => s.IpAddress == LeadArrange.Address(nonce));

        // A boolean would not be demonstrable. The words, their version, the purpose, the time and
        // the address are what make the record evidence (BR-LEAD-06).
        submission.Consent!.ConsentText.Should().Be(form.ConsentText);
        submission.Consent.ConsentVersion.Should().Be(form.ConsentVersion);
        submission.Consent.Purpose.Should().NotBeEmpty();
        submission.Consent.GivenAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task REQ_LEAD_007_The_same_enquiry_sent_twice_in_a_minute_is_one_lead_and_one_acknowledgement()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var first = await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", LeadArrange.ContactBody(nonce));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var firstReference = (await first.Content.ReadFromJsonAsync<LeadArrange.SubmitRow>())!.Reference;

        // The same content again, with a fresh token: a person who double-clicked, not a replay.
        var again = LeadArrange.ContactBody(nonce);
        again.CaptchaToken = LeadArrange.Token(nonce + "-second");

        var second = await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", again);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // They see the reference they already have, not a new one.
        (await second.Content.ReadFromJsonAsync<LeadArrange.SubmitRow>())!.Reference.Should().Be(firstReference);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var leads = await db.Leads.AsNoTracking().Where(l => l.FullName == $"PROBE-{nonce}").ToListAsync();
        leads.Should().ContainSingle();

        var acknowledgements = await db.OutboxEmails.AsNoTracking()
            .CountAsync(o => o.RelatedEntityId == leads[0].Id && o.TemplateKey == EmailTemplate.LeadAcknowledgement);

        acknowledgements.Should().Be(1);
    }

    [Fact]
    public async Task REQ_LEAD_008_A_lead_carrying_campaign_parameters_records_them_and_its_source()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var body = LeadArrange.ContactBody(nonce);
        body.Utm = new Dictionary<string, string>
        {
            ["utm_source"] = "google",
            ["utm_medium"] = "cpc",
            ["utm_campaign"] = $"hms-{nonce}",
        };

        (await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", body))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.FullName == $"PROBE-{nonce}");

        lead.Source.Should().Be(LeadSource.Campaign);
        lead.UtmJson.Should().Contain($"hms-{nonce}");
    }

    [Fact]
    public async Task REQ_LEAD_008_A_lead_with_no_campaign_parameters_is_recorded_as_direct_rather_than_unknown()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        (await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", LeadArrange.ContactBody(nonce)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.FullName == $"PROBE-{nonce}");

        // "We do not know" and "they typed the address" are different answers, and a report that
        // conflates them misleads.
        lead.Source.Should().Be(LeadSource.Direct);
        lead.UtmJson.Should().BeNull();
    }

    [Fact]
    public async Task REQ_LEAD_001_Only_a_signed_in_user_with_the_lead_permission_can_read_the_enquiries()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        (await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", LeadArrange.ContactBody(nonce)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var anonymous = _fixture.CreateClient();
        (await anonymous.GetAsync("/api/v1/leads")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The editor maintains the site, not the pipeline. Personal data is not theirs to read.
        var editor = await _fixture.ClientAsAsync(LeadFixture.EditorEmail);
        (await editor.GetAsync("/api/v1/leads")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);
        var visible = await sales.GetFromJsonAsync<List<LeadArrange.LeadRow>>("/api/v1/leads");
        visible!.Should().Contain(l => l.FullName == $"PROBE-{nonce}");
    }
}
