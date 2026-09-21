using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Application.Leads;
using SoftwareManagement.Application.Notifications;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Leads;
using SoftwareManagement.Domain.Notifications;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Leads;

/// <summary>
/// The two things nobody is sitting there to notice: a follow-up that has come due, and a
/// conversation that has gone quiet.
///
/// Both end in a queued email rather than a sent one, so this can run on a timer without a mail
/// server being reachable, and both are written so that running the sweep twice sends nothing
/// twice. That is the property that matters: a reminder that arrives every five minutes until
/// somebody clicks something is a reminder people filter out.
/// </summary>
public sealed partial class LeadSweep(
    AppDbContext dbContext,
    IEmailOutbox outbox,
    ISystemSettings settings,
    IClock clock,
    ILogger<LeadSweep> logger) : ILeadSweep
{
    /// <summary>Days of silence after which a contacted lead is shown as stale (BR-LEAD-11).</summary>
    public const int StaleAfterDays = 14;

    /// <summary>And after which the owner is told once, rather than shown a flag they may not visit.</summary>
    public const int RemindAfterDays = 30;

    /// <summary>How many of each kind one pass handles, so a backlog cannot make a pass run long.</summary>
    public const int BatchSize = 50;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly IEmailOutbox _outbox = outbox;
    private readonly ISystemSettings _settings = settings;
    private readonly IClock _clock = clock;
    private readonly ILogger<LeadSweep> _logger = logger;

    public async Task<SweepResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var notifyTo = _settings.Value("notify.ownerEmail");

        if (string.IsNullOrWhiteSpace(notifyTo))
        {
            // Nowhere to send. Saying so once a pass is better than queueing messages addressed to
            // nobody, which the outbox would refuse one at a time and quietly.
            LogNoOwnerAddress(_logger);
            return new SweepResult(0, 0);
        }

        var reminders = await SendDueRemindersAsync(now, notifyTo, cancellationToken).ConfigureAwait(false);
        var cold = await NudgeColdLeadsAsync(now, notifyTo, cancellationToken).ConfigureAwait(false);

        return new SweepResult(reminders, cold);
    }

    private async Task<int> SendDueRemindersAsync(DateTime now, string notifyTo, CancellationToken cancellationToken)
    {
        var due = await _dbContext.LeadActivities
            .Include(a => a.Lead)
            .Where(a => a.DueAtUtc != null && a.DueAtUtc <= now && !a.IsCompleted)
            .OrderBy(a => a.DueAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var queued = 0;

        foreach (var reminder in due)
        {
            if (reminder.Lead is null)
            {
                continue;
            }

            var result = await _outbox.QueueAsync(
                EmailTemplate.LeadFollowUpDue,
                notifyTo,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["fullName"] = reminder.Lead.FullName,
                    ["note"] = reminder.Body ?? "No note was written.",
                    ["contact"] = Contactable(reminder.Lead),
                    ["createdAt"] = reminder.Lead.CreatedAtUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                },
                correlationId: reminder.Id,
                relatedEntityType: nameof(Lead),
                relatedEntityId: reminder.LeadId,
                cancellationToken).ConfigureAwait(false);

            // Marked done whether or not the message was composed. A reminder that cannot be
            // composed will not compose on the next pass either, and leaving it due would make the
            // sweep retry it every minute forever.
            reminder.IsCompleted = true;
            reminder.ModifiedBy = "sweep";

            if (result.Queued)
            {
                queued++;
            }
        }

        if (due.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return queued;
    }

    private async Task<int> NudgeColdLeadsAsync(DateTime now, string notifyTo, CancellationToken cancellationToken)
    {
        var threshold = now.AddDays(-RemindAfterDays);

        // Contacted and then nothing: no activity at all since the threshold, and nobody told yet.
        // The "told yet" column is what makes a second pass send nothing (REQ-LEAD-014).
        var cold = await _dbContext.Leads
            .Where(l => l.Stage == LeadStage.Contacted
                && l.StaleReminderSentAtUtc == null
                && l.CreatedAtUtc <= threshold
                && !l.Activities.Any(a => a.OccurredAtUtc > threshold))
            .OrderBy(l => l.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var queued = 0;

        foreach (var lead in cold)
        {
            var result = await _outbox.QueueAsync(
                EmailTemplate.LeadGoneCold,
                notifyTo,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["fullName"] = lead.FullName,
                    ["days"] = RemindAfterDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["contact"] = Contactable(lead),
                    ["createdAt"] = lead.CreatedAtUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                },
                correlationId: lead.Id,
                relatedEntityType: nameof(Lead),
                relatedEntityId: lead.Id,
                cancellationToken).ConfigureAwait(false);

            lead.StaleReminderSentAtUtc = now;
            lead.ModifiedBy = "sweep";

            _dbContext.LeadActivities.Add(new LeadActivity
            {
                Id = Guid.NewGuid(),
                LeadId = lead.Id,
                ActivityType = LeadActivityType.SystemEvent,
                Direction = LeadActivityDirection.Internal,
                Body = $"No activity for {RemindAfterDays} days; the owner was reminded.",
                OccurredAtUtc = now,
                CreatedBy = "sweep",
            });

            if (result.Queued)
            {
                queued++;
            }
        }

        if (cold.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogColdLeads(_logger, cold.Count);
        }

        return queued;
    }

    private static string Contactable(Lead lead) =>
        lead.Email ?? lead.Phone ?? "no contact details";

    [LoggerMessage(Level = LogLevel.Warning, Message = "No owner address is configured, so follow-up reminders cannot be sent. Set notify.ownerEmail.")]
    private static partial void LogNoOwnerAddress(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "{count} lead(s) had gone quiet and the owner was reminded.")]
    private static partial void LogColdLeads(ILogger logger, int count);
}

/// <summary>
/// Runs the sweep on a timer.
///
/// Every few minutes rather than every few seconds: nothing here is urgent to the minute, and a
/// reminder due at 10:00 arriving at 10:04 is the same reminder.
/// </summary>
public sealed partial class LeadSweepService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<LeadSweepService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<LeadSweepService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("LeadSweep:Enabled", true))
        {
            LogSweepDisabled(_logger);
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(30, _configuration.GetValue("LeadSweep:IntervalSeconds", 300)));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sweep = scope.ServiceProvider.GetRequiredService<ILeadSweep>();
                await sweep.RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or TimeoutException)
            {
                // One failed pass is not a reason to stop reminding anyone ever again.
                LogPassFailed(_logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The lead sweep is disabled by configuration. Reminders will not be queued.")]
    private static partial void LogSweepDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "A lead sweep pass failed. The next pass will pick up the same work.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
