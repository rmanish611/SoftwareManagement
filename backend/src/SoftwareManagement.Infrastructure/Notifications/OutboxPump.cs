using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Notifications;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Notifications;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Notifications;

/// <summary>
/// Drains the outbox.
///
/// A failure is not a lost message: the attempt is recorded with whatever the mail server said, and
/// the message is rescheduled at 1, 5, 15, 60 and then 240 minutes. After the fifth failure it is
/// dead-lettered and the owner is told, because a message that quietly stops being retried is
/// exactly the enquiry that never gets answered (BR-NOTIF-02).
/// </summary>
public sealed partial class OutboxPump(
    AppDbContext dbContext,
    IEmailSender sender,
    IClock clock,
    ILogger<OutboxPump> logger) : IOutboxPump
{
    /// <summary>How many messages one pass takes. Small, so a pass is short and a failure is cheap.</summary>
    public const int BatchSize = 20;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly IEmailSender _sender = sender;
    private readonly IClock _clock = clock;
    private readonly ILogger<OutboxPump> _logger = logger;

    public async Task<OutboxPassResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var due = await _dbContext.OutboxEmails
            .Where(o => o.Status == OutboxStatus.Pending && o.NextAttemptAtUtc <= now)
            .OrderBy(o => o.NextAttemptAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var sent = 0;
        var rescheduled = 0;
        var deadLettered = 0;

        foreach (var message in due)
        {
            var attemptNumber = message.AttemptCount + 1;
            var stopwatch = Stopwatch.StartNew();

            var outcome = await _sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            _dbContext.EmailDeliveryLogs.Add(new EmailDeliveryLog
            {
                Id = Guid.NewGuid(),
                OutboxEmailId = message.Id,
                AttemptNumber = attemptNumber,
                AttemptedAtUtc = _clock.UtcNow,
                SmtpStatusCode = outcome.StatusCode,
                SmtpResponse = Truncate(outcome.Response, 1000),
                Succeeded = outcome.Succeeded,
                DurationMs = outcome.DurationMs,
            });

            message.AttemptCount = attemptNumber;

            if (outcome.Succeeded)
            {
                message.Status = OutboxStatus.Sent;
                message.SentAtUtc = _clock.UtcNow;
                message.LastError = null;
                sent++;
                continue;
            }

            message.LastError = Truncate(outcome.Response, 1000);
            var nextAttempt = OutboxEmail.NextAttemptAfter(attemptNumber, _clock.UtcNow);

            if (nextAttempt is null)
            {
                message.Status = OutboxStatus.DeadLettered;
                deadLettered++;
                var maskedRecipient = PrivacyMask.Email(message.ToAddress);
                LogDeadLettered(_logger, message.TemplateKey, maskedRecipient, attemptNumber);
                await AlertOwnerAsync(message, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                message.NextAttemptAtUtc = nextAttempt.Value;
                rescheduled++;
            }
        }

        if (due.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new OutboxPassResult(due.Count, sent, rescheduled, deadLettered);
    }

    /// <summary>
    /// Tells the owner a message has been given up on. The alert is a plain row rather than a
    /// composed template, so a broken template cannot stop the warning that a template is broken.
    /// </summary>
    private async Task AlertOwnerAsync(OutboxEmail failed, CancellationToken cancellationToken)
    {
        var ownerAddress = await _dbContext.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key == "notify.ownerEmail")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(ownerAddress))
        {
            return;
        }

        // Guard against the alert about a failed alert failing and alerting again, forever.
        if (string.Equals(failed.TemplateKey, EmailTemplate.OutboxDeadLettered, StringComparison.Ordinal))
        {
            return;
        }

        var subject = $"A message could not be delivered after {failed.AttemptCount} attempts";
        var body =
            $"The message \"{failed.Subject}\" to {PrivacyMask.Email(failed.ToAddress)} was not delivered.\n\n" +
            $"Template: {failed.TemplateKey}\n" +
            $"Last error: {failed.LastError}\n\n" +
            "It will not be retried again. The enquiry itself is safe in the system.";

        _dbContext.OutboxEmails.Add(new OutboxEmail
        {
            Id = Guid.NewGuid(),
            TemplateKey = EmailTemplate.OutboxDeadLettered,
            ToAddress = ownerAddress,
            Subject = subject,
            HtmlBody = "<pre>" + System.Net.WebUtility.HtmlEncode(body) + "</pre>",
            TextBody = body,
            Status = OutboxStatus.Pending,
            NextAttemptAtUtc = _clock.UtcNow,
            CorrelationId = failed.CorrelationId,
            RelatedEntityType = nameof(OutboxEmail),
            RelatedEntityId = failed.Id,
            CreatedBy = "system",
        });
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    [LoggerMessage(Level = LogLevel.Error, Message = "The message {templateKey} to {recipient} was given up on after {attempts} attempts.")]
    private static partial void LogDeadLettered(ILogger logger, string templateKey, string recipient, int attempts);
}

/// <summary>
/// Runs the pump on a timer.
///
/// The interval is short because the thing being delayed is an acknowledgement to someone who has
/// just pressed a button and is watching for a reply. A pass that throws is logged and the timer
/// continues: a background service that dies silently would stop every email on the site.
/// </summary>
public sealed partial class OutboxPumpService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<OutboxPumpService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<OutboxPumpService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("Outbox:PumpEnabled", true))
        {
            LogPumpDisabled(_logger);
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _configuration.GetValue("Outbox:PumpIntervalSeconds", 30)));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var pump = scope.ServiceProvider.GetRequiredService<IOutboxPump>();
                await pump.RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or TimeoutException)
            {
                // The pass failed; the next one will pick the same messages up. Stopping the timer
                // would stop every email on the site until a restart.
                LogPassFailed(_logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The outbox pump is disabled by configuration. Messages will queue and not be sent.")]
    private static partial void LogPumpDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "An outbox pass failed. The messages remain queued and the next pass will retry them.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
