using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Application.Leads;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Domain.Leads;
using SoftwareManagement.Domain.Notifications;
using SoftwareManagement.Infrastructure.Leads;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Leads;

/// <summary>
/// The clock and the two things nobody is sitting there to notice.
///
/// The SLA arithmetic is exercised against the real calculator with the real settings and the real
/// holiday table, because the whole point of the rule is that it agrees with what the office
/// actually does (BR-LEAD-10).
/// </summary>
[Collection(LeadTestGroup.Name)]
public sealed class SlaAndSweepTests(LeadFixture fixture)
{
    private readonly LeadFixture _fixture = fixture;

    [Fact]
    public void REQ_LEAD_013_A_Saturday_night_enquiry_answered_on_Monday_morning_took_one_business_hour()
    {
        using var scope = _fixture.NewScope();
        var sla = scope.ServiceProvider.GetRequiredService<ISlaCalculator>();
        var zone = scope.ServiceProvider.GetRequiredService<IEditorTimeZone>();

        // 23:55 on a Saturday, and 10:00 on the Monday. Saturday is a working day here and the
        // office shuts at 18:00, so nothing of Saturday night counts; Sunday is not a working day;
        // Monday opens at 09:00. One hour (BR-LEAD-10, EX-193).
        var received = zone.ToUtc(new DateTime(2026, 3, 7, 23, 55, 0, DateTimeKind.Unspecified));
        var answered = zone.ToUtc(new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Unspecified));

        sla.BusinessTimeBetween(received, answered).Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void REQ_LEAD_013_Wall_clock_hours_and_business_hours_are_not_the_same_number()
    {
        using var scope = _fixture.NewScope();
        var sla = scope.ServiceProvider.GetRequiredService<ISlaCalculator>();
        var zone = scope.ServiceProvider.GetRequiredService<IEditorTimeZone>();

        var received = zone.ToUtc(new DateTime(2026, 3, 7, 23, 55, 0, DateTimeKind.Unspecified));
        var answered = zone.ToUtc(new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Unspecified));

        // The check that the test above is measuring something. Thirty-four hours passed on the
        // wall, and reporting that number is how an SLA stops being believed.
        (answered - received).Should().BeGreaterThan(TimeSpan.FromHours(34));
    }

    [Fact]
    public void REQ_LEAD_013_An_answer_inside_the_same_working_day_is_counted_in_full()
    {
        using var scope = _fixture.NewScope();
        var sla = scope.ServiceProvider.GetRequiredService<ISlaCalculator>();
        var zone = scope.ServiceProvider.GetRequiredService<IEditorTimeZone>();

        var received = zone.ToUtc(new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Unspecified));
        var answered = zone.ToUtc(new DateTime(2026, 3, 10, 12, 30, 0, DateTimeKind.Unspecified));

        sla.BusinessTimeBetween(received, answered).Should().Be(TimeSpan.FromHours(2.5));
    }

    [Fact]
    public async Task REQ_LEAD_013_A_holiday_is_skipped_by_the_clock_as_a_closed_day()
    {
        var nonce = LeadArrange.Nonce();

        // A Wednesday nobody is in. Added as a one-off rather than recurring, which is what a
        // festival that moves every year has to be.
        var holiday = new DateOnly(2026, 3, 11);
        await AddHolidayAsync(holiday, $"Test closure {nonce}");

        using var scope = _fixture.NewScope();
        var sla = scope.ServiceProvider.GetRequiredService<ISlaCalculator>();
        var zone = scope.ServiceProvider.GetRequiredService<IEditorTimeZone>();

        // Tuesday 17:00 to Thursday 10:00. Without the holiday that is one hour of Tuesday, nine
        // of Wednesday and one of Thursday; with it, Wednesday does not count.
        var received = zone.ToUtc(new DateTime(2026, 3, 10, 17, 0, 0, DateTimeKind.Unspecified));
        var answered = zone.ToUtc(new DateTime(2026, 3, 12, 10, 0, 0, DateTimeKind.Unspecified));

        sla.BusinessTimeBetween(received, answered).Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task REQ_LEAD_013_A_deadline_lands_after_the_holiday_rather_than_on_it()
    {
        var nonce = LeadArrange.Nonce();
        var holiday = new DateOnly(2026, 4, 15);
        await AddHolidayAsync(holiday, $"Test closure {nonce}");

        using var scope = _fixture.NewScope();
        var sla = scope.ServiceProvider.GetRequiredService<ISlaCalculator>();
        var zone = scope.ServiceProvider.GetRequiredService<IEditorTimeZone>();

        // 17:55 the day before the office shuts for a day. Five minutes of today are left, so the
        // remaining eight hours and fifty-five minutes fall on the next day that is open, which is
        // the day after the holiday (REQ-ADM-002).
        var received = zone.ToUtc(new DateTime(2026, 4, 14, 17, 55, 0, DateTimeKind.Unspecified));
        var due = zone.ToEditorLocal(sla.FirstResponseDueUtc(received));

        DateOnly.FromDateTime(due).Should().Be(new DateOnly(2026, 4, 16));
    }

    [Fact]
    public async Task REQ_LEAD_012_A_reminder_that_has_come_due_queues_one_message_and_not_a_second()
    {
        var nonce = LeadArrange.Nonce();
        var leadId = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        await sales.PostAsJsonAsync($"/api/v1/leads/{leadId}/activities", new
        {
            activityType = "Note",
            direction = "Internal",
            body = $"Ring them back about the {nonce} rollout.",
            dueAtUtc = DateTime.UtcNow.AddMinutes(-1),
        });

        var first = await RunSweepAsync();
        first.RemindersQueued.Should().Be(1);

        using (var scope = _fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            (await db.OutboxEmails.AsNoTracking()
                .CountAsync(o => o.TemplateKey == EmailTemplate.LeadFollowUpDue && o.RelatedEntityId == leadId))
                .Should().Be(1);

            // Marked done, which is what stops the next pass sending it again. A reminder that
            // arrives every five minutes until somebody clicks something is one people filter out.
            (await db.LeadActivities.AsNoTracking().SingleAsync(a => a.LeadId == leadId && a.DueAtUtc != null))
                .IsCompleted.Should().BeTrue();
        }

        var second = await RunSweepAsync();
        second.RemindersQueued.Should().Be(0);

        using var after = _fixture.NewScope();
        var database = after.ServiceProvider.GetRequiredService<AppDbContext>();

        (await database.OutboxEmails.AsNoTracking()
            .CountAsync(o => o.TemplateKey == EmailTemplate.LeadFollowUpDue && o.RelatedEntityId == leadId))
            .Should().Be(1);
    }

    [Fact]
    public async Task REQ_LEAD_012_A_reminder_that_is_not_due_yet_is_left_alone()
    {
        var nonce = LeadArrange.Nonce();
        var leadId = await _fixture.AddLeadAsync(nonce);
        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);

        await sales.PostAsJsonAsync($"/api/v1/leads/{leadId}/activities", new
        {
            activityType = "Note",
            direction = "Internal",
            body = "Next week some time.",
            dueAtUtc = DateTime.UtcNow.AddDays(7),
        });

        await RunSweepAsync();

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.OutboxEmails.AsNoTracking()
            .AnyAsync(o => o.TemplateKey == EmailTemplate.LeadFollowUpDue && o.RelatedEntityId == leadId))
            .Should().BeFalse();

        (await db.LeadActivities.AsNoTracking().SingleAsync(a => a.LeadId == leadId && a.DueAtUtc != null))
            .IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task REQ_LEAD_014_A_contacted_lead_with_a_month_of_silence_reminds_the_owner_once()
    {
        var nonce = LeadArrange.Nonce();
        var leadId = await _fixture.AddLeadAsync(nonce, createdAtUtc: DateTime.UtcNow.AddDays(-40));

        await SetStageAsync(leadId, LeadStage.Contacted, DateTime.UtcNow.AddDays(-35));

        var first = await RunSweepAsync();
        first.ColdLeadsQueued.Should().Be(1);

        using (var scope = _fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            (await db.OutboxEmails.AsNoTracking()
                .CountAsync(o => o.TemplateKey == EmailTemplate.LeadGoneCold && o.RelatedEntityId == leadId))
                .Should().Be(1);

            // The timeline says why the owner got an email, which is the difference between a
            // system that nags and one that explains itself.
            (await db.LeadActivities.AsNoTracking()
                .AnyAsync(a => a.LeadId == leadId && a.ActivityType == LeadActivityType.SystemEvent))
                .Should().BeTrue();
        }

        var second = await RunSweepAsync();
        second.ColdLeadsQueued.Should().Be(0);
    }

    [Fact]
    public async Task REQ_LEAD_014_A_lead_that_was_touched_last_week_is_not_cold()
    {
        var nonce = LeadArrange.Nonce();
        var leadId = await _fixture.AddLeadAsync(nonce, createdAtUtc: DateTime.UtcNow.AddDays(-40));

        await SetStageAsync(leadId, LeadStage.Contacted, DateTime.UtcNow.AddDays(-3));

        var result = await RunSweepAsync();

        result.ColdLeadsQueued.Should().Be(0);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId))
            .StaleReminderSentAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task REQ_LEAD_014_A_lead_still_in_New_is_the_SLA_s_problem_and_not_the_stale_sweep_s()
    {
        var nonce = LeadArrange.Nonce();
        await _fixture.AddLeadAsync(nonce, createdAtUtc: DateTime.UtcNow.AddDays(-60));

        // Nobody has contacted them at all, which is a breached first response rather than a
        // conversation that went quiet. Two different failures, two different reports (BR-LEAD-11).
        var result = await RunSweepAsync();

        result.ColdLeadsQueued.Should().Be(0);
    }

    [Fact]
    public async Task REQ_LEAD_009_A_contacted_lead_with_a_fortnight_of_silence_is_flagged_stale_in_the_inbox()
    {
        var nonce = LeadArrange.Nonce();
        var leadId = await _fixture.AddLeadAsync(nonce, createdAtUtc: DateTime.UtcNow.AddDays(-30));

        await SetStageAsync(leadId, LeadStage.Contacted, DateTime.UtcNow.AddDays(-20));

        var sales = await _fixture.ClientAsAsync(LeadFixture.SalesEmail);
        var stale = await sales.GetFromJsonAsync<List<StaleRow>>("/api/v1/leads?view=stale&pageSize=100");

        stale!.Should().Contain(l => l.Id == leadId);
        stale.Single(l => l.Id == leadId).IsStale.Should().BeTrue();
    }

    private async Task<SweepResult> RunSweepAsync()
    {
        using var scope = _fixture.NewScope();
        return await scope.ServiceProvider.GetRequiredService<ILeadSweep>().RunOnceAsync(CancellationToken.None);
    }

    /// <summary>
    /// Moves a lead to a stage and backdates its only activity, which is how a lead that was
    /// contacted a month ago and then forgotten is arranged without waiting a month.
    /// </summary>
    private async Task SetStageAsync(Guid leadId, LeadStage stage, DateTime lastTouchedUtc)
    {
        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.LeadActivities.Add(new LeadActivity
        {
            Id = Guid.NewGuid(),
            LeadId = leadId,
            ActivityType = LeadActivityType.StageChange,
            Direction = LeadActivityDirection.Internal,
            Body = "Contacted.",
            OccurredAtUtc = lastTouchedUtc,
            FromStage = LeadStage.New,
            ToStage = stage,
            CreatedBy = "test-fixture",
        });

        await db.SaveChangesAsync();

        await db.Leads.Where(l => l.Id == leadId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Stage, stage));
    }

    private async Task AddHolidayAsync(DateOnly date, string name)
    {
        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.Holidays.AnyAsync(h => h.HolidayDate == date))
        {
            return;
        }

        db.Holidays.Add(new Holiday
        {
            Id = Guid.NewGuid(),
            HolidayDate = date,
            Name = name,
            IsRecurring = false,
            CreatedBy = "test-fixture",
        });

        await db.SaveChangesAsync();

        // The calendar is cached for ten minutes so the SLA clock does not go to the database once
        // per day examined. A holiday added mid-test would otherwise not be seen by this test.
        scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()
            .Remove("holidays:all");
    }

    private sealed record StaleRow(Guid Id, string FullName, string Stage, bool IsStale);
}
