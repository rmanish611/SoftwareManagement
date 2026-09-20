using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Notifications;
using SoftwareManagement.Domain.Notifications;
using SoftwareManagement.Infrastructure.Notifications;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Leads;

/// <summary>
/// The outbox: nothing sent inline, nothing lost, and nothing half-composed reaching a customer.
/// </summary>
[Collection(LeadTestGroup.Name)]
public sealed class NotificationTests(LeadFixture fixture)
{
    private readonly LeadFixture _fixture = fixture;

    [Fact]
    public async Task REQ_NOTIF_001_A_new_lead_queues_both_messages_in_the_same_transaction_and_sends_nothing_inline()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var response = await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", LeadArrange.ContactBody(nonce));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.FullName == $"PROBE-{nonce}");
        var queued = await db.OutboxEmails.AsNoTracking().Where(o => o.RelatedEntityId == lead.Id).ToListAsync();

        queued.Should().HaveCount(2);
        queued.Select(o => o.TemplateKey).Should().Contain(EmailTemplate.LeadAcknowledgement);
        queued.Select(o => o.TemplateKey).Should().Contain(EmailTemplate.LeadOwnerAlert);

        // Nothing was sent during the request: every message is still waiting, with no attempts
        // against it. That is what stops a slow mail server failing a visitor's form.
        queued.Should().OnlyContain(o => o.Status == OutboxStatus.Pending && o.AttemptCount == 0);
    }

    [Fact]
    public async Task REQ_NOTIF_001_A_submission_that_was_refused_leaves_nothing_behind_in_the_queue()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var before = await CountOutboxAsync();

        var body = LeadArrange.ContactBody(nonce);
        body.Consent = false;

        var response = await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", body);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The other half of BR-NOTIF-01. The first test proves a lead cannot exist without its
        // messages; this one proves the messages cannot exist without a lead. An acknowledgement
        // going out for an enquiry that was refused is a message to someone who never wrote in.
        (await db.Leads.AnyAsync(l => l.FullName == $"PROBE-{nonce}")).Should().BeFalse();
        (await CountOutboxAsync()).Should().Be(before);
    }

    private async Task<int> CountOutboxAsync()
    {
        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.OutboxEmails.AsNoTracking().CountAsync();
    }

    [Fact]
    public async Task REQ_NOTIF_003_The_acknowledgement_carries_the_persons_name_and_their_reference()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        var response = await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", LeadArrange.ContactBody(nonce));
        var reference = (await response.Content.ReadFromJsonAsync<LeadArrange.SubmitRow>())!.Reference;

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.FullName == $"PROBE-{nonce}");
        var acknowledgement = await db.OutboxEmails.AsNoTracking()
            .SingleAsync(o => o.RelatedEntityId == lead.Id && o.TemplateKey == EmailTemplate.LeadAcknowledgement);

        acknowledgement.ToAddress.Should().Be($"probe-{nonce}@example.test");
        acknowledgement.Subject.Should().Contain(reference);
        acknowledgement.TextBody.Should().Contain($"PROBE-{nonce}");
        acknowledgement.HtmlBody.Should().Contain(reference);

        // Nothing left unfilled. A message going out with a visible placeholder is the failure the
        // composer refuses.
        acknowledgement.TextBody.Should().NotContain("{{");
        acknowledgement.HtmlBody.Should().NotContain("{{");
    }

    [Fact]
    public async Task REQ_NOTIF_003_A_template_with_a_placeholder_nobody_filled_is_refused_rather_than_queued()
    {
        using var scope = _fixture.NewScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        var template = new EmailTemplate
        {
            Key = "test.incomplete",
            Subject = "Hello {{fullName}}",
            TextBody = "Your reference is {{reference}} and your plan is {{planName}}.",
            HtmlBody = "<p>{{reference}}</p>",
            PlaceholdersJson = """["fullName","reference","planName"]""",
        };

        var composed = composer.Compose(template, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fullName"] = "Anita",
            ["reference"] = "ENQ-12345678",
        });

        composed.Success.Should().BeFalse();
        composed.Problem.Should().Contain("planName");
    }

    [Fact]
    public async Task REQ_NOTIF_009_A_composed_message_containing_something_shaped_like_a_credential_is_refused()
    {
        using var scope = _fixture.NewScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        var template = new EmailTemplate
        {
            Key = "test.leaky",
            Subject = "Your demo",
            TextBody = "Sign in with {{details}}",
            HtmlBody = "<p>{{details}}</p>",
            PlaceholdersJson = """["details"]""",
        };

        var composed = composer.Compose(template, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["details"] = "username demo, password: hunter2demo",
        });

        // An email is forwarded, quoted and archived by people who were never meant to have it.
        composed.Success.Should().BeFalse();
        composed.Problem.Should().Contain("credential");
    }

    [Fact]
    public async Task REQ_NOTIF_004_A_failed_send_increases_the_attempt_count_and_schedules_the_next_one()
    {
        var nonce = LeadArrange.Nonce();
        var message = await QueueFailingMessageAsync(nonce);

        var first = await RunPumpAsync(alwaysFail: true);
        first.Rescheduled.Should().BeGreaterThan(0);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var after = await db.OutboxEmails.AsNoTracking().SingleAsync(o => o.Id == message);

        after.AttemptCount.Should().Be(1);
        after.Status.Should().Be(OutboxStatus.Pending);
        after.LastError.Should().NotBeNullOrEmpty();

        // The first retry is a minute out, which is the first entry in the schedule.
        after.NextAttemptAtUtc.Should().BeAfter(DateTime.UtcNow.AddSeconds(30));

        var log = await db.EmailDeliveryLogs.AsNoTracking().SingleAsync(l => l.OutboxEmailId == message);
        log.AttemptNumber.Should().Be(1);
        log.Succeeded.Should().BeFalse();
        log.SmtpResponse.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task REQ_NOTIF_004_The_fifth_failure_dead_letters_the_message_and_alerts_the_owner()
    {
        var nonce = LeadArrange.Nonce();
        var message = await QueueFailingMessageAsync(nonce);

        // Five attempts. Between each one the message is made due again, which is what the clock
        // would do; the schedule itself is asserted in the test above.
        for (var attempt = 1; attempt <= OutboxEmail.MaxAttempts; attempt++)
        {
            await MakeDueAsync(message);
            await RunPumpAsync(alwaysFail: true);
        }

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dead = await db.OutboxEmails.AsNoTracking().SingleAsync(o => o.Id == message);
        dead.Status.Should().Be(OutboxStatus.DeadLettered);
        dead.AttemptCount.Should().Be(OutboxEmail.MaxAttempts);

        var attempts = await db.EmailDeliveryLogs.AsNoTracking().CountAsync(l => l.OutboxEmailId == message);
        attempts.Should().Be(OutboxEmail.MaxAttempts);

        // A message that quietly stops being retried is the enquiry that never gets answered, so a
        // person is told.
        var alert = await db.OutboxEmails.AsNoTracking()
            .Where(o => o.TemplateKey == EmailTemplate.OutboxDeadLettered && o.RelatedEntityId == message)
            .SingleAsync();

        alert.Subject.Should().Contain("could not be delivered");
    }

    [Fact]
    public async Task REQ_NOTIF_004_A_message_that_sends_is_marked_sent_and_never_tried_again()
    {
        var nonce = LeadArrange.Nonce();
        var message = await QueueFailingMessageAsync(nonce);

        await RunPumpAsync(alwaysFail: false);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sent = await db.OutboxEmails.AsNoTracking().SingleAsync(o => o.Id == message);

        sent.Status.Should().Be(OutboxStatus.Sent);
        sent.SentAtUtc.Should().NotBeNull();
        sent.AttemptCount.Should().Be(1);

        // A second pass must not pick it up again: the pump takes pending messages only.
        var second = await RunPumpAsync(alwaysFail: false);
        second.Considered.Should().Be(0);
    }

    [Fact]
    public async Task REQ_NOTIF_002_The_envelope_sender_is_the_company_and_never_the_person_who_wrote_in()
    {
        var nonce = LeadArrange.Nonce();
        using var client = _fixture.ClientFrom(LeadArrange.Address(nonce));

        (await client.PostAsJsonAsync("/api/v1/public/forms/contact/submit", LeadArrange.ContactBody(nonce)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.FullName == $"PROBE-{nonce}");
        var alert = await db.OutboxEmails.AsNoTracking()
            .SingleAsync(o => o.RelatedEntityId == lead.Id && o.TemplateKey == EmailTemplate.LeadOwnerAlert);

        // The queued row never carries a sender: the sender is decided at send time from the
        // company's own configuration, so the enquirer's address can never become it (BR-NOTIF-03).
        alert.ToAddress.Should().Be(LeadFixture.OwnerEmail);
        alert.ToAddress.Should().NotBe($"probe-{nonce}@example.test");

        var from = db.SystemSettings.AsNoTracking().Single(s => s.Key == "notify.fromEmail");
        from.Value.Should().NotBeNullOrEmpty();
        from.Value.Should().NotContain("example.test");
    }

    [Fact]
    public void NFR_PRIV_03_An_email_address_is_masked_before_it_can_reach_a_log()
    {
        PrivacyMask.Email("anita@example.test").Should().Be("a***@example.test");
        PrivacyMask.Email("a@b.test").Should().Be("a***@b.test");
        PrivacyMask.Email("not-an-address").Should().Be("***");
        PrivacyMask.Email(null).Should().Be("(none)");

        // The domain survives because it is what makes a delivery problem diagnosable, and it names
        // an organisation rather than a person.
        PrivacyMask.Email("someone@hospital.example").Should().EndWith("@hospital.example");
    }

    [Fact]
    public void NFR_PRIV_03_An_address_and_a_phone_number_keep_only_what_a_diagnosis_needs()
    {
        PrivacyMask.IpAddress("203.0.113.42").Should().Be("203.0.113.x");
        PrivacyMask.IpAddress("2001:db8:1234::1").Should().Be("2001:db8::x");
        PrivacyMask.IpAddress(null).Should().Be("(none)");

        PrivacyMask.Phone("+91 98765 43210").Should().Be("***10");
        PrivacyMask.Phone("12").Should().Be("***");
        PrivacyMask.Phone(null).Should().Be("(none)");
    }

    /// <summary>
    /// Queues one message directly, so the retry behaviour can be exercised without going through a
    /// form. The address is inside the example domain and belongs to nobody.
    ///
    /// Everything already queued is parked first. A pass takes twenty due messages oldest-first
    /// (<see cref="OutboxPump.BatchSize"/>), and every accepted enquiry in the capture tests queues
    /// two, so by the time these tests run the backlog is larger than a batch - and this message,
    /// being the newest, sorts last and would never be reached. The test would then report that the
    /// pump failed to retry when what actually happened is that it never saw the message.
    ///
    /// Parking rather than deleting: those messages are evidence other tests assert on.
    /// </summary>
    private async Task<Guid> QueueFailingMessageAsync(string nonce)
    {
        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.OutboxEmails
            .Where(o => o.Status == OutboxStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.NextAttemptAtUtc, DateTime.UtcNow.AddDays(1)));

        var message = new OutboxEmail
        {
            Id = Guid.NewGuid(),
            TemplateKey = "test.retry",
            ToAddress = $"retry-{nonce}@example.test",
            Subject = $"Retry probe {nonce}",
            HtmlBody = "<p>Retry probe.</p>",
            TextBody = "Retry probe.",
            Status = OutboxStatus.Pending,
            NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(-1),
            CorrelationId = Guid.NewGuid(),
            CreatedBy = "test",
        };

        db.OutboxEmails.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private async Task MakeDueAsync(Guid messageId)
    {
        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.OutboxEmails
            .Where(o => o.Id == messageId && o.Status == OutboxStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(-1)));
    }

    /// <summary>
    /// One pump pass against a sender whose answer the test chooses. The real SMTP sender is not
    /// used: a test that needed a mail server would be a test of the mail server.
    /// </summary>
    private async Task<OutboxPassResult> RunPumpAsync(bool alwaysFail)
    {
        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<SoftwareManagement.Domain.Common.IClock>();

        var pump = new OutboxPump(
            db,
            new ScriptedSender(alwaysFail),
            clock,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OutboxPump>>());

        return await pump.RunOnceAsync(CancellationToken.None);
    }

    /// <summary>A mail server that answers the same way every time, so a test can decide the outcome.</summary>
    private sealed class ScriptedSender(bool alwaysFail) : IEmailSender
    {
        public Task<SendOutcome> SendAsync(OutboxEmail message, CancellationToken cancellationToken) =>
            Task.FromResult(alwaysFail
                ? new SendOutcome(false, "451", "Temporary local problem, please try again later.", 12)
                : new SendOutcome(true, "250", "Accepted", 9));
    }
}
