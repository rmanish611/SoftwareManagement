using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Notifications;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Notifications;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Notifications;

/// <summary>
/// Adds a composed message to the outbox, in the caller's unit of work.
///
/// It deliberately does not save. The caller saves, which is what makes the message and the thing
/// that caused it a single commit: a lead cannot exist without its acknowledgement queued, and a
/// rolled-back lead leaves no orphan message behind (BR-NOTIF-01).
/// </summary>
public sealed partial class OutboxWriter(
    AppDbContext dbContext,
    IEmailComposer composer,
    IClock clock,
    ILogger<OutboxWriter> logger) : IEmailOutbox
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IEmailComposer _composer = composer;
    private readonly IClock _clock = clock;
    private readonly ILogger<OutboxWriter> _logger = logger;

    public async Task<QueueResult> QueueAsync(
        string templateKey,
        string toAddress,
        IReadOnlyDictionary<string, string> values,
        Guid correlationId,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toAddress))
        {
            return QueueResult.Refused("no recipient");
        }

        var template = await _dbContext.EmailTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Key == templateKey && t.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (template is null)
        {
            LogTemplateMissing(_logger, templateKey);
            return QueueResult.Refused("no such template");
        }

        var composed = _composer.Compose(template, values);

        if (!composed.Success)
        {
            // The message is not queued, and the reason is recorded with the masked recipient. A
            // half-formed message reaching a customer is the failure this refusal prevents.
            var maskedRecipient = PrivacyMask.Email(toAddress);
            LogComposeRefused(_logger, templateKey, maskedRecipient, composed.Problem ?? "unknown");
            return QueueResult.Refused(composed.Problem ?? "could not compose");
        }

        var message = new OutboxEmail
        {
            Id = Guid.NewGuid(),
            TemplateKey = templateKey,
            ToAddress = toAddress,
            Subject = composed.Subject,
            HtmlBody = composed.HtmlBody,
            TextBody = composed.TextBody,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            NextAttemptAtUtc = _clock.UtcNow,
            CorrelationId = correlationId,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedBy = "system",
        };

        _dbContext.OutboxEmails.Add(message);
        return QueueResult.Ok(message);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "No active email template named {templateKey}. Nothing was queued.")]
    private static partial void LogTemplateMissing(ILogger logger, string templateKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "The template {templateKey} could not be composed for {recipient}: {problem}")]
    private static partial void LogComposeRefused(ILogger logger, string templateKey, string recipient, string problem);
}
