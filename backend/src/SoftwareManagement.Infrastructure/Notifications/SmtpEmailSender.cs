using System.Diagnostics;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Application.Notifications;
using SoftwareManagement.Domain.Notifications;

namespace SoftwareManagement.Infrastructure.Notifications;

/// <summary>
/// Sends a message over SMTP.
///
/// The envelope sender is always an address at the company's own domain, never the enquirer's. A
/// message claiming to be from the person who filled in the form fails SPF at the receiving end and
/// is filed as spam or forgery, which is how a site quietly stops being able to email anyone
/// (BR-NOTIF-03).
/// </summary>
public sealed partial class SmtpEmailSender(
    IConfiguration configuration,
    ISystemSettings settings,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly IConfiguration _configuration = configuration;
    private readonly ISystemSettings _settings = settings;
    private readonly ILogger<SmtpEmailSender> _logger = logger;

    public async Task<SendOutcome> SendAsync(OutboxEmail message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var host = _configuration["Smtp:Host"];
        var fromAddress = _configuration["Smtp:FromAddress"] ?? _settings.Value("notify.fromEmail");

        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
        {
            stopwatch.Stop();

            // Not configured is a failure, not a success. Reporting it as sent would mark the
            // message delivered and lose it (NFR-AVAIL-03).
            LogNotConfigured(_logger);
            return new SendOutcome(false, "NOT_CONFIGURED", "No SMTP host or sender address is configured.", (int)stopwatch.ElapsedMilliseconds);
        }

        try
        {
            using var client = new SmtpClient(host, _configuration.GetValue("Smtp:Port", 587))
            {
                EnableSsl = _configuration.GetValue("Smtp:UseSsl", true),
                Timeout = _configuration.GetValue("Smtp:TimeoutMs", 15000),
            };

            var user = _configuration["Smtp:Username"];
            var pass = _configuration["Smtp:Password"];

            if (!string.IsNullOrWhiteSpace(user))
            {
                client.Credentials = new System.Net.NetworkCredential(user, pass);
            }

            using var mail = new MailMessage
            {
                From = new MailAddress(fromAddress, _settings.Value("company.name") ?? "Software Management"),
                Subject = message.Subject,
                Body = message.HtmlBody,
                IsBodyHtml = true,
            };

            mail.To.Add(message.ToAddress);

            if (!string.IsNullOrWhiteSpace(message.CcAddress))
            {
                mail.CC.Add(message.CcAddress);
            }

            // The plain-text alternative is attached rather than dropped: some clients prefer it,
            // and a message with no text part scores worse with spam filters.
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.TextBody, null, "text/plain"));

            await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return new SendOutcome(true, "250", "Accepted by " + host, (int)stopwatch.ElapsedMilliseconds);
        }
        catch (SmtpException exception)
        {
            stopwatch.Stop();
            var maskedRecipient = PrivacyMask.Email(message.ToAddress);
            LogSendFailed(_logger, maskedRecipient, exception);
            return new SendOutcome(false, exception.StatusCode.ToString(), exception.Message, (int)stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or IOException)
        {
            stopwatch.Stop();
            var maskedRecipient = PrivacyMask.Email(message.ToAddress);
            LogSendFailed(_logger, maskedRecipient, exception);
            return new SendOutcome(false, "ERROR", exception.Message, (int)stopwatch.ElapsedMilliseconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No SMTP host or sender address is configured; queued messages cannot be sent.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending to {recipient} failed. The message stays queued and will be retried.")]
    private static partial void LogSendFailed(ILogger logger, string recipient, Exception exception);
}
