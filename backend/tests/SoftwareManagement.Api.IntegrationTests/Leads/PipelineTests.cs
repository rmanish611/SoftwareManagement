using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Leads;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Leads;

/// <summary>
/// Working an enquiry after it arrives: moving it, writing down what happened, and the three
/// operations that change what a lead is rather than where it is.
///
/// Everything here goes in through the admin API with a token that carries the permission, because
/// the rules being tested are enforced there and a test that called the service directly would
/// prove the rule exists without proving anyone is subject to it.
/// </summary>
[Collection(LeadTestGroup.Name)]
public sealed class PipelineTests(LeadFixture fixture)
{
    private readonly LeadFixture _fixture = fixture;

    [Fact]
    public async Task REQ_LEAD_010_Moving_a_lead_forward_appends_a_stage_change_naming_both_stages_and_the_actor()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var moved = await sales.PostAsJsonAsync($"/api/v1/leads/{id}/stage", new { stage = "Contacted" });
        moved.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Leads.AsNoTracking().SingleAsync(l => l.Id == id)).Stage.Should().Be(LeadStage.Contacted);

        var activity = await db.LeadActivities.AsNoTracking()
            .SingleAsync(a => a.LeadId == id && a.ActivityType == LeadActivityType.StageChange);

        // Both stages and who did it. "The stage is Contacted" is a fact; "Sales moved it from New
        // on Tuesday" is what somebody reading this in six months actually needs.
        activity.FromStage.Should().Be(LeadStage.New);
        activity.ToStage.Should().Be(LeadStage.Contacted);
        activity.CreatedBy.Should().Be(LeadFixture.SalesEmail);
    }

    [Fact]
    public async Task REQ_LEAD_010_Disqualifying_with_a_four_character_reason_is_refused_and_says_how_long_it_must_be()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var response = await sales.PostAsJsonAsync(
            $"/api/v1/leads/{id}/stage", new { stage = "Disqualified", reason = "junk" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<LeadArrange.ProblemRow>();
        problem!.Field.Should().Be("reason");
        problem.Detail.Should().Contain("10");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Leads.AsNoTracking().SingleAsync(l => l.Id == id)).Stage.Should().Be(LeadStage.New);
    }

    [Fact]
    public async Task REQ_LEAD_010_Disqualifying_with_a_real_reason_records_it_on_the_lead_and_in_the_timeline()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        const string Reason = "Budget is a tenth of what the smallest plan costs.";

        (await sales.PostAsJsonAsync($"/api/v1/leads/{id}/stage", new { stage = "Disqualified", reason = Reason }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.Id == id);
        lead.Stage.Should().Be(LeadStage.Disqualified);
        lead.DisqualifyReason.Should().Be(Reason);

        (await db.LeadActivities.AsNoTracking()
            .SingleAsync(a => a.LeadId == id && a.ActivityType == LeadActivityType.StageChange))
            .Body.Should().Be(Reason);
    }

    [Fact]
    public async Task REQ_LEAD_010_Skipping_a_stage_without_saying_why_is_refused()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        // New straight to Qualified skips Contacted. It happens - somebody rings in already sold -
        // but the funnel has to say that is what happened (BR-LEAD-09).
        var refused = await sales.PostAsJsonAsync($"/api/v1/leads/{id}/stage", new { stage = "Qualified" });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadFromJsonAsync<LeadArrange.ProblemRow>())!.Field.Should().Be("reason");

        var allowed = await sales.PostAsJsonAsync(
            $"/api/v1/leads/{id}/stage",
            new { stage = "Qualified", reason = "Rang in having already used the product elsewhere." });

        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task REQ_LEAD_010_A_stage_that_is_not_a_stage_is_refused_rather_than_ignored()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var response = await sales.PostAsJsonAsync($"/api/v1/leads/{id}/stage", new { stage = "Interested" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("UNKNOWN_STAGE");
    }

    [Fact]
    public async Task REQ_LEAD_011_A_call_note_is_appended_with_when_it_happened_and_who_wrote_it()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var occurred = DateTime.UtcNow.AddHours(-3);

        var created = await sales.PostAsJsonAsync($"/api/v1/leads/{id}/activities", new
        {
            activityType = "Call",
            direction = "Outbound",
            body = $"Spoke for ten minutes about the {nonce} rollout.",
            occurredAtUtc = occurred,
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activity = await db.LeadActivities.AsNoTracking()
            .SingleAsync(a => a.LeadId == id && a.ActivityType == LeadActivityType.Call);

        // Friday's call logged on Monday is recorded as Friday. The alternative teaches people to
        // write "(actually Friday)" in the body, where no report can read it.
        activity.OccurredAtUtc.Should().BeCloseTo(occurred, TimeSpan.FromSeconds(2));
        activity.CreatedBy.Should().Be(LeadFixture.SalesEmail);
    }

    [Fact]
    public async Task REQ_LEAD_011_An_activity_cannot_be_deleted_and_the_refusal_says_what_to_do_instead()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var created = await sales.PostAsJsonAsync($"/api/v1/leads/{id}/activities", new
        {
            activityType = "Note",
            direction = "Internal",
            body = "Something worth remembering.",
        });

        var activityId = (await created.Content.ReadFromJsonAsync<CreatedRow>())!.Id;

        var deleted = await sales.DeleteAsync($"/api/v1/leads/{id}/activities/{activityId}");

        deleted.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await deleted.Content.ReadAsStringAsync()).Should().Contain("APPEND_ONLY");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.LeadActivities.AsNoTracking().AnyAsync(a => a.Id == activityId)).Should().BeTrue();
    }

    [Fact]
    public async Task REQ_LEAD_011_An_empty_note_is_refused_because_it_records_nothing()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var response = await sales.PostAsJsonAsync($"/api/v1/leads/{id}/activities", new
        {
            activityType = "Note",
            direction = "Internal",
            body = "   ",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<LeadArrange.ProblemRow>())!.Field.Should().Be("body");
    }

    [Fact]
    public async Task REQ_LEAD_011_A_stage_change_cannot_be_posted_by_hand_into_the_timeline()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        // Otherwise the timeline could be made to say a lead was qualified when it never moved.
        var response = await sales.PostAsJsonAsync($"/api/v1/leads/{id}/activities", new
        {
            activityType = "StageChange",
            direction = "Internal",
            body = "Pretending this happened.",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ACTIVITY_NOT_ALLOWED");
    }

    [Fact]
    public async Task REQ_LEAD_013_An_outbound_reply_stops_the_clock_and_a_private_note_does_not()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        await sales.PostAsJsonAsync($"/api/v1/leads/{id}/activities", new
        {
            activityType = "Note",
            direction = "Internal",
            body = "Will ring them after lunch.",
        });

        using (var scope = _fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Intending to reply is not replying, and a response-time report that counts it is
            // flattering and useless (BR-LEAD-10).
            (await db.Leads.AsNoTracking().SingleAsync(l => l.Id == id)).FirstResponseAtUtc.Should().BeNull();
        }

        await sales.PostAsJsonAsync($"/api/v1/leads/{id}/activities", new
        {
            activityType = "EmailOut",
            direction = "Outbound",
            body = "Sent them the pricing and a demo link.",
        });

        using var after = _fixture.NewScope();
        var database = after.ServiceProvider.GetRequiredService<AppDbContext>();
        (await database.Leads.AsNoTracking().SingleAsync(l => l.Id == id)).FirstResponseAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task REQ_LEAD_013_The_first_reply_is_the_one_that_counts_and_a_later_one_does_not_move_it()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var first = DateTime.UtcNow.AddHours(-2);

        await sales.PostAsJsonAsync($"/api/v1/leads/{id}/activities", new
        {
            activityType = "Call",
            direction = "Outbound",
            body = "First reply.",
            occurredAtUtc = first,
        });

        await sales.PostAsJsonAsync($"/api/v1/leads/{id}/activities", new
        {
            activityType = "EmailOut",
            direction = "Outbound",
            body = "Following up.",
        });

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Leads.AsNoTracking().SingleAsync(l => l.Id == id))
            .FirstResponseAtUtc.Should().BeCloseTo(first, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task REQ_LEAD_011_An_activity_cannot_have_happened_in_the_future()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        // The clock would otherwise stop before the reply existed, and the SLA report would show a
        // negative response time.
        var response = await sales.PostAsJsonAsync($"/api/v1/leads/{id}/activities", new
        {
            activityType = "Call",
            direction = "Outbound",
            body = "Tomorrow's call, logged today.",
            occurredAtUtc = DateTime.UtcNow.AddDays(1),
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<LeadArrange.ProblemRow>())!.Field.Should().Be("occurredAtUtc");
    }

    [Fact]
    public async Task REQ_LEAD_016_Merging_keeps_the_earliest_lead_and_moves_the_other_s_activities_onto_it()
    {
        var nonce = LeadArrange.Nonce();
        var older = await _fixture.AddLeadAsync(nonce, createdAtUtc: DateTime.UtcNow.AddDays(-3));
        var newer = await _fixture.AddLeadAsync(LeadArrange.Nonce(), createdAtUtc: DateTime.UtcNow.AddHours(-1));

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        await sales.PostAsJsonAsync($"/api/v1/leads/{newer}/activities", new
        {
            activityType = "Note",
            direction = "Internal",
            body = $"Written against the newer record, {nonce}.",
        });

        // Named newer-first on purpose: the survivor is chosen by the rule, not by the order the
        // caller happened to type them in (BR-LEAD-08).
        var merged = await sales.PostAsJsonAsync($"/api/v1/leads/{newer}/merge", new { otherLeadId = older });
        merged.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var survivor = await db.Leads.AsNoTracking().SingleAsync(l => l.Id == older);
        var absorbed = await db.Leads.AsNoTracking().SingleAsync(l => l.Id == newer);

        survivor.Stage.Should().NotBe(LeadStage.Merged);
        absorbed.Stage.Should().Be(LeadStage.Merged);
        absorbed.MergedIntoLeadId.Should().Be(older);

        (await db.LeadActivities.AsNoTracking()
            .AnyAsync(a => a.LeadId == older && a.Body!.Contains(nonce))).Should().BeTrue();
    }

    [Fact]
    public async Task REQ_LEAD_016_A_merged_lead_is_left_out_of_the_inbox_and_of_every_count()
    {
        var nonce = LeadArrange.Nonce();
        var older = await _fixture.AddLeadAsync(nonce, createdAtUtc: DateTime.UtcNow.AddDays(-2));
        var newer = await _fixture.AddLeadAsync($"{nonce}-dup", createdAtUtc: DateTime.UtcNow.AddHours(-1));

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);
        await sales.PostAsJsonAsync($"/api/v1/leads/{older}/merge", new { otherLeadId = newer });

        var inbox = await sales.GetFromJsonAsync<List<LeadArrange.LeadRow>>("/api/v1/leads?pageSize=100");

        inbox!.Should().Contain(l => l.Id == older);
        inbox.Should().NotContain(l => l.Id == newer);
    }

    [Fact]
    public async Task REQ_LEAD_016_Merging_a_lead_into_itself_is_refused()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var response = await sales.PostAsJsonAsync($"/api/v1/leads/{id}/merge", new { otherLeadId = id });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MERGE_SELF");
    }

    [Fact]
    public async Task REQ_LEAD_016_Merging_the_same_pair_twice_is_a_conflict_rather_than_a_second_merge()
    {
        var nonce = LeadArrange.Nonce();
        var older = await _fixture.AddLeadAsync(nonce, createdAtUtc: DateTime.UtcNow.AddDays(-2));
        var newer = await _fixture.AddLeadAsync($"{nonce}-again", createdAtUtc: DateTime.UtcNow.AddHours(-1));

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);
        await sales.PostAsJsonAsync($"/api/v1/leads/{older}/merge", new { otherLeadId = newer });

        var again = await sales.PostAsJsonAsync($"/api/v1/leads/{older}/merge", new { otherLeadId = newer });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).Should().Contain("ALREADY_MERGED");
    }

    [Fact]
    public async Task REQ_LEAD_015_Converting_creates_the_company_and_the_person_and_links_both_to_the_lead()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var converted = await sales.PostAsJsonAsync($"/api/v1/leads/{id}/convert", new
        {
            organisationName = $"Northwind {nonce}",
            contactFullName = "Priya Sharma",
            contactEmail = $"priya-{nonce}@northwind.test",
            contactJobTitle = "Operations lead",
        });

        converted.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lead = await db.Leads.AsNoTracking()
            .Include(l => l.Organisation).Include(l => l.Contact)
            .SingleAsync(l => l.Id == id);

        lead.Stage.Should().Be(LeadStage.Converted);
        lead.Organisation!.DisplayName.Should().Be($"Northwind {nonce}");
        lead.Contact!.Email.Should().Be($"priya-{nonce}@northwind.test");

        // The first person at a new company is the one to ring until somebody says otherwise.
        lead.Contact.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task REQ_LEAD_015_Converting_with_no_company_at_all_is_refused()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce, companyName: null);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var response = await sales.PostAsJsonAsync($"/api/v1/leads/{id}/convert", new
        {
            contactFullName = "Nobody In Particular",
            contactEmail = $"nobody-{nonce}@example.test",
        });

        // A customer record with a blank company name is worse than no customer record.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<LeadArrange.ProblemRow>())!.Field.Should().Be("organisationId");
    }

    [Fact]
    public async Task REQ_LEAD_015_Converting_a_second_lead_onto_the_same_company_reuses_it()
    {
        var nonce = LeadArrange.Nonce();
        var first = await _fixture.AddLeadAsync(nonce);
        var second = await _fixture.AddLeadAsync($"{nonce}-b");
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var created = await sales.PostAsJsonAsync($"/api/v1/leads/{first}/convert", new
        {
            organisationName = $"Contoso {nonce}",
            contactEmail = $"one-{nonce}@contoso.test",
        });

        var organisationId = (await created.Content.ReadFromJsonAsync<ConvertedRow>())!.OrganisationId;

        var reused = await sales.PostAsJsonAsync($"/api/v1/leads/{second}/convert", new
        {
            organisationId,
            contactEmail = $"two-{nonce}@contoso.test",
        });

        reused.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Organisations.AsNoTracking().CountAsync(o => o.DisplayName == $"Contoso {nonce}")).Should().Be(1);
        (await db.Contacts.AsNoTracking().CountAsync(c => c.OrganisationId == organisationId)).Should().Be(2);

        // The second person is not made primary over the first.
        (await db.Contacts.AsNoTracking().CountAsync(c => c.OrganisationId == organisationId && c.IsPrimary))
            .Should().Be(1);
    }

    [Fact]
    public async Task REQ_LEAD_015_A_lead_cannot_be_converted_twice()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        await sales.PostAsJsonAsync($"/api/v1/leads/{id}/convert", new
        {
            organisationName = $"Fabrikam {nonce}",
            contactEmail = $"one-{nonce}@fabrikam.test",
        });

        var again = await sales.PostAsJsonAsync($"/api/v1/leads/{id}/convert", new
        {
            organisationName = $"Fabrikam {nonce} again",
            contactEmail = $"two-{nonce}@fabrikam.test",
        });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).Should().Contain("ALREADY_CONVERTED");
    }

    [Fact]
    public async Task REQ_LEAD_018_Marking_spam_hides_the_lead_and_restoring_it_resumes_the_original_clock()
    {
        var nonce = LeadArrange.Nonce();
        var created = DateTime.UtcNow.AddDays(-5);
        var id = await _fixture.AddLeadAsync(nonce, createdAtUtc: created);

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);
        var owner = await _fixture.ClientAsAsync(LeadFixture.OwnerEmail);

        (await sales.PostAsync($"/api/v1/leads/{id}/spam", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var hidden = await sales.GetFromJsonAsync<List<LeadArrange.LeadRow>>("/api/v1/leads?pageSize=100");
        hidden!.Should().NotContain(l => l.Id == id);

        (await owner.PostAsync($"/api/v1/leads/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.Id == id);

        lead.Stage.Should().Be(LeadStage.New);

        // Somebody wrote in five days ago and was wrongly binned. The report should show five days
        // of delay, because that is what happened (REQ-LEAD-018).
        lead.SlaDueAtUtc.Should().BeBefore(DateTime.UtcNow);

        (await db.LeadActivities.AsNoTracking()
            .CountAsync(a => a.LeadId == id && a.ActivityType == LeadActivityType.StageChange)).Should().Be(2);
    }

    [Fact]
    public async Task REQ_LEAD_018_Only_the_owner_may_undo_somebody_else_s_spam_decision()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);
        await sales.PostAsync($"/api/v1/leads/{id}/spam", null);

        // Marking spam is tidying up. Undoing somebody else's is a decision about their judgement,
        // and the matrix gives it to the Owner alone (AZ-35).
        (await sales.PostAsync($"/api/v1/leads/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var owner = await _fixture.ClientAsAsync(LeadFixture.OwnerEmail);
        (await owner.PostAsync($"/api/v1/leads/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task REQ_LEAD_017_A_lead_from_the_same_company_domain_is_offered_as_a_possible_duplicate()
    {
        var nonce = LeadArrange.Nonce();

        // Different company names on purpose. The domain is the only thing these two share, so a
        // match here can only have come from the domain rule (BR-CUST-04).
        var first = await _fixture.AddLeadAsync(
            nonce, email: $"amit-{nonce}@acme-{nonce}.test", companyName: $"Acme {nonce}");

        var second = await _fixture.AddLeadAsync(
            $"{nonce}-b", email: $"sunita-{nonce}@acme-{nonce}.test", companyName: $"Acme {nonce} Logistics");

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);
        var suggestions = await sales.GetFromJsonAsync<List<DuplicateRow>>($"/api/v1/leads/{second}/duplicates");

        var match = suggestions!.Should().ContainSingle(s => s.Id == first).Subject;

        // A sentence a person can act on, not a score. "Another address at acme.test" is checkable;
        // "0.82" is not.
        match.Reason.Should().Contain($"acme-{nonce}.test");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Suggested, never merged. Nothing was touched by asking the question (REQ-LEAD-017).
        (await db.Leads.AsNoTracking().SingleAsync(l => l.Id == first)).Stage.Should().Be(LeadStage.New);
    }

    [Fact]
    public async Task REQ_LEAD_017_A_lead_with_nothing_in_common_suggests_nothing()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce, email: $"solo-{nonce}@nowhere-{nonce}.test", companyName: null);

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);
        var suggestions = await sales.GetFromJsonAsync<List<DuplicateRow>>($"/api/v1/leads/{id}/duplicates");

        // A suggestion list that is never empty is one nobody reads.
        suggestions!.Should().BeEmpty();
    }

    [Fact]
    public async Task REQ_LEAD_009_The_inbox_puts_the_enquiry_waiting_longest_at_the_top()
    {
        var nonce = LeadArrange.Nonce();

        var answered = await _fixture.AddLeadAsync($"{nonce}-answered", createdAtUtc: DateTime.UtcNow.AddDays(-4));
        var waiting = await _fixture.AddLeadAsync($"{nonce}-waiting", createdAtUtc: DateTime.UtcNow.AddDays(-3));

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        await sales.PostAsJsonAsync($"/api/v1/leads/{answered}/activities", new
        {
            activityType = "EmailOut",
            direction = "Outbound",
            body = "Answered this one.",
        });

        var inbox = await sales.GetFromJsonAsync<List<InboxRow>>("/api/v1/leads?pageSize=100");

        var waitingAt = inbox!.FindIndex(l => l.Id == waiting);
        var answeredAt = inbox.FindIndex(l => l.Id == answered);

        // The older enquiry has been answered; the newer one has not. Sorting by arrival alone
        // would bury the one the company is currently failing (REQ-LEAD-009).
        waitingAt.Should().BeGreaterThanOrEqualTo(0);
        answeredAt.Should().BeGreaterThanOrEqualTo(0);
        waitingAt.Should().BeLessThan(answeredAt);

        inbox[waitingAt].IsBreached.Should().BeTrue();
        inbox[waitingAt].MinutesToSlaDue.Should().BeNegative();
    }

    [Fact]
    public async Task REQ_LEAD_009_The_saved_views_answer_the_three_questions_the_screen_is_opened_to_ask()
    {
        var nonce = LeadArrange.Nonce();
        var breached = await _fixture.AddLeadAsync(nonce, createdAtUtc: DateTime.UtcNow.AddDays(-6));

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var unanswered = await sales.GetFromJsonAsync<List<InboxRow>>("/api/v1/leads?view=unanswered&pageSize=100");
        unanswered!.Should().Contain(l => l.Id == breached);
        unanswered.Should().OnlyContain(l => l.FirstResponseAtUtc == null);

        var late = await sales.GetFromJsonAsync<List<InboxRow>>("/api/v1/leads?view=breached&pageSize=100");
        late!.Should().Contain(l => l.Id == breached);
        late.Should().OnlyContain(l => l.IsBreached);

        await sales.PostAsJsonAsync($"/api/v1/leads/{breached}/activities", new
        {
            activityType = "Call",
            direction = "Outbound",
            body = "Rang them back.",
        });

        var afterReply = await sales.GetFromJsonAsync<List<InboxRow>>("/api/v1/leads?view=unanswered&pageSize=100");
        afterReply!.Should().NotContain(l => l.Id == breached);
    }

    [Fact]
    public async Task REQ_LEAD_009_The_inbox_can_be_searched_by_name_company_or_message()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce, companyName: $"Distinctive {nonce} Logistics");

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        var byCompany = await sales.GetFromJsonAsync<List<InboxRow>>($"/api/v1/leads?search=Distinctive {nonce}");
        byCompany!.Should().ContainSingle(l => l.Id == id);

        var byNothing = await sales.GetFromJsonAsync<List<InboxRow>>($"/api/v1/leads?search=no-such-thing-{nonce}");
        byNothing!.Should().BeEmpty();
    }

    [Fact]
    public async Task NFR_AUTHZ_02_The_editor_is_refused_everywhere_in_the_pipeline()
    {
        var nonce = LeadArrange.Nonce();
        var id = await _fixture.AddLeadAsync(nonce);

        var editor = await _fixture.ClientAsAsync(LeadFixture.EditorEmail);

        // The editor maintains the site. Leads are personal data about other people and none of it
        // is theirs to read or change (AZ-30, AZ-31, AZ-36).
        (await editor.GetAsync("/api/v1/leads")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.GetAsync($"/api/v1/leads/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.GetAsync($"/api/v1/leads/{id}/duplicates")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await editor.PostAsJsonAsync($"/api/v1/leads/{id}/stage", new { stage = "Contacted" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await editor.PostAsJsonAsync($"/api/v1/leads/{id}/activities", new
        {
            activityType = "Note",
            direction = "Internal",
            body = "Should never be written.",
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.LeadActivities.AsNoTracking().AnyAsync(a => a.LeadId == id)).Should().BeFalse();
    }

    private sealed record CreatedRow(Guid Id);

    private sealed record ConvertedRow(Guid OrganisationId);

    private sealed record DuplicateRow(Guid Id, string FullName, string? Email, string? CompanyName, string Stage, string Reason);

    private sealed record InboxRow(
        Guid Id,
        string FullName,
        string Stage,
        DateTime CreatedAtUtc,
        DateTime SlaDueAtUtc,
        DateTime? FirstResponseAtUtc,
        bool IsBreached,
        int? MinutesToSlaDue,
        bool IsStale);
}
