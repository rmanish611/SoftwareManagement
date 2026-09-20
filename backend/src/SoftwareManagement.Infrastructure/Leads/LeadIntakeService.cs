using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Application.Leads;
using SoftwareManagement.Application.Notifications;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Leads;
using SoftwareManagement.Domain.Notifications;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Leads;

/// <summary>
/// What happens when a stranger presses Send.
///
/// The order of the checks is the design. The cheap refusals come first, so a flood costs almost
/// nothing: the honeypot and the rate limit are decided before the captcha service is called, and
/// the captcha before anything is written. The expensive, irreversible part, writing a submission, a
/// consent record, a lead and two queued emails, happens once, in one transaction, at the end
/// (REQ-LEAD-001, BR-NOTIF-01).
/// </summary>
public sealed partial class LeadIntakeService(
    AppDbContext dbContext,
    ICaptchaVerifier captcha,
    ISubmissionRateLimiter rateLimiter,
    ISlaCalculator sla,
    IEmailOutbox emails,
    ISystemSettings settings,
    IClock clock,
    ILogger<LeadIntakeService> logger) : ILeadIntakeService
{
    /// <summary>A repeat of the same content from the same address inside this window is one enquiry.</summary>
    public const int DuplicateWindowMinutes = 10;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly ICaptchaVerifier _captcha = captcha;
    private readonly ISubmissionRateLimiter _rateLimiter = rateLimiter;
    private readonly ISlaCalculator _sla = sla;
    private readonly IEmailOutbox _emails = emails;
    private readonly ISystemSettings _settings = settings;
    private readonly IClock _clock = clock;
    private readonly ILogger<LeadIntakeService> _logger = logger;

    public async Task<IntakeResult> SubmitAsync(IntakeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var form = await _dbContext.FormDefinitions
            .Include(f => f.Fields)
            .FirstOrDefaultAsync(f => f.Key == request.FormKey, cancellationToken).ConfigureAwait(false);

        if (form is null)
        {
            return IntakeResult.Failed(IntakeOutcome.FormNotFound, "There is no such form.");
        }

        if (!form.IsEnabled)
        {
            return IntakeResult.Failed(
                IntakeOutcome.FormClosed,
                "This form is closed at the moment. Please email us instead and we will reply.");
        }

        // A hidden field a person cannot see. Anything in it came from a machine, so the answer is
        // the ordinary one and the row is filed as spam: telling a bot what caught it teaches the
        // next one (BR-LEAD-03).
        var trippedHoneypot = !string.IsNullOrWhiteSpace(request.HoneypotValue);

        if (!trippedHoneypot)
        {
            var limit = await _rateLimiter.CheckAsync(request.IpAddress, cancellationToken).ConfigureAwait(false);

            if (!limit.Allowed)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    var maskedIp = PrivacyMask.IpAddress(request.IpAddress);
                    LogRateLimited(_logger, maskedIp, limit.RetryAfterSeconds);
                }
                return IntakeResult.Failed(
                    IntakeOutcome.RateLimited,
                    "That is a lot of enquiries from one place. Please wait a little and try again.",
                    limit.RetryAfterSeconds);
            }
        }

        if (!request.Consent)
        {
            // Nothing is stored. A record of someone who did not agree is the thing consent exists
            // to prevent (BR-LEAD-06).
            return IntakeResult.Failed(
                IntakeOutcome.ConsentRequired,
                "Please tick the box to say we may contact you about this enquiry.");
        }

        var validation = Validate(form, request.Answers);
        if (validation is not null)
        {
            return validation;
        }

        var captchaResult = trippedHoneypot
            ? CaptchaVerification.Rejected("honeypot")
            : await _captcha.VerifyAsync(request.CaptchaToken, request.IpAddress, cancellationToken).ConfigureAwait(false);

        if (!trippedHoneypot && !captchaResult.Passed)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var maskedIp = PrivacyMask.IpAddress(request.IpAddress);
                LogCaptchaRefused(_logger, maskedIp, captchaResult.Reason);
            }
            return IntakeResult.Failed(
                IntakeOutcome.CaptchaInvalid,
                "We could not confirm that you are a person. Please reload the page and try again.");
        }

        var fullName = Answer(request.Answers, "fullName") ?? Answer(request.Answers, "name") ?? "Someone";
        var email = Answer(request.Answers, "email");
        var phone = Answer(request.Answers, "phone");
        var message = Answer(request.Answers, "message");
        var company = Answer(request.Answers, "companyName") ?? Answer(request.Answers, "company");

        var hash = ContentHash(form.Key, email, phone, message);

        // A double-click is one enquiry. The visitor is shown the same reference as the first time,
        // and no second acknowledgement is queued (BR-LEAD-05).
        if (!trippedHoneypot)
        {
            var since = _clock.UtcNow.AddMinutes(-DuplicateWindowMinutes);

            var earlier = await _dbContext.FormSubmissions
                .AsNoTracking()
                .Where(s => s.MessageHash == hash && s.CreatedAtUtc >= since && s.LeadId != null)
                .OrderByDescending(s => s.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (earlier?.LeadId is { } existingLeadId)
            {
                return IntakeResult.Duplicate(existingLeadId, Reference(existingLeadId), form.SuccessMessage);
            }
        }

        var product = await ResolveProductAsync(request.Answers, cancellationToken).ConfigureAwait(false);

        var submission = new FormSubmission
        {
            Id = Guid.NewGuid(),
            FormDefinitionId = form.Id,
            PayloadJson = JsonSerializer.Serialize(request.Answers),
            MessageHash = hash,
            IpAddress = request.IpAddress,
            UserAgent = Truncate(request.UserAgent, 400),
            Referrer = Truncate(request.Referrer, 500),
            CaptchaOutcome = trippedHoneypot ? CaptchaOutcome.Failed : CaptchaOutcome.Passed,
            IsSpam = trippedHoneypot,
            CreatedBy = "public",
        };

        submission.Consent = new ConsentRecord
        {
            Id = Guid.NewGuid(),
            FormSubmissionId = submission.Id,
            ConsentText = form.ConsentText,
            ConsentVersion = form.ConsentVersion,
            Purpose = "Replying to this enquiry and contacting you about it.",
            GivenAtUtc = _clock.UtcNow,
            IpAddress = request.IpAddress,
            CreatedBy = "public",
        };

        _dbContext.FormSubmissions.Add(submission);

        if (trippedHoneypot)
        {
            // Stored, never notified, never counted, and answered exactly as a real enquiry is.
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var maskedIp = PrivacyMask.IpAddress(request.IpAddress);
                LogHoneypotTripped(_logger, maskedIp);
            }
            return IntakeResult.Spam(Reference(submission.Id), form.SuccessMessage);
        }

        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            Email = email,
            Phone = phone,
            CompanyName = company,
            Message = message,
            ProductId = product,
            Stage = LeadStage.New,
            Source = SourceOf(request.Utm, request.Referrer),
            UtmJson = request.Utm.Count > 0 ? JsonSerializer.Serialize(request.Utm) : null,
            OwnerUserId = await DefaultOwnerAsync(cancellationToken).ConfigureAwait(false),
            SlaDueAtUtc = _sla.FirstResponseDueUtc(_clock.UtcNow),
            CreatedBy = "public",
        };

        _dbContext.Leads.Add(lead);
        submission.LeadId = lead.Id;

        var reference = Reference(lead.Id);
        await QueueNotificationsAsync(form, lead, reference, cancellationToken).ConfigureAwait(false);

        // One commit. The submission, the consent, the lead and both queued emails are a single fact
        // about the world, and a half-written version of it is worse than a refusal.
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            var maskedEmail = PrivacyMask.Email(email);
            LogLeadCreated(_logger, reference, maskedEmail, form.Key);
        }
        return IntakeResult.Accepted(lead.Id, reference, form.SuccessMessage);
    }

    /// <summary>
    /// Checks the answers against what the form says it needs. The required set is the definition's,
    /// not this code's, so adding a question is a data change (REQ-LEAD-002).
    /// </summary>
    private static IntakeResult? Validate(FormDefinition form, IReadOnlyDictionary<string, string?> answers)
    {
        foreach (var field in form.Fields.OrderBy(f => f.SortOrder))
        {
            var value = Answer(answers, field.Name);

            if (field.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                return IntakeResult.Invalid($"{field.Label} is needed.", field.Name);
            }

            if (value is not null && field.MaxLength is { } max && value.Length > max)
            {
                return IntakeResult.Invalid($"{field.Label} is longer than {max} characters.", field.Name);
            }

            if (field.FieldType == FormFieldType.Email && !string.IsNullOrWhiteSpace(value) && !LooksLikeEmail(value))
            {
                return IntakeResult.Invalid("That email address does not look right.", field.Name);
            }

            if (field.FieldType == FormFieldType.Phone && !string.IsNullOrWhiteSpace(value) && !LooksLikeIndianMobile(value))
            {
                return IntakeResult.Invalid("That phone number does not look like a 10-digit mobile number.", field.Name);
            }
        }

        var email = Answer(answers, "email");
        var phone = Answer(answers, "phone");

        // The one rule no form definition may relax: an enquiry nobody can reply to is not an
        // enquiry (BR-LEAD-01).
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
        {
            return IntakeResult.Invalid("Please leave either an email address or a phone number, so we can reply.");
        }

        return null;
    }

    private async Task QueueNotificationsAsync(FormDefinition form, Lead lead, string reference, CancellationToken cancellationToken)
    {
        var correlationId = lead.Id;
        var companyName = _settings.Value("company.name") ?? "Software Management";

        if (!string.IsNullOrWhiteSpace(lead.Email))
        {
            await _emails.QueueAsync(
                form.AcknowledgementTemplateKey ?? EmailTemplate.LeadAcknowledgement,
                lead.Email,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["fullName"] = lead.FullName,
                    ["reference"] = reference,
                    ["companyName"] = companyName,
                },
                correlationId,
                nameof(Lead),
                lead.Id,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var recipient in form.NotifyEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await _emails.QueueAsync(
                EmailTemplate.LeadOwnerAlert,
                recipient,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["fullName"] = lead.FullName,
                    ["reference"] = reference,
                    ["formTitle"] = form.Title,

                    // The alert carries the enquiry itself: the owner reads it on a phone and
                    // decides whether it can wait.
                    ["message"] = lead.Message ?? "(no message)",
                    ["contact"] = lead.Email ?? lead.Phone ?? "(none given)",
                    ["dueAt"] = lead.SlaDueAtUtc.ToString("u", CultureInfo.InvariantCulture),
                },
                correlationId,
                nameof(Lead),
                lead.Id,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Guid?> ResolveProductAsync(IReadOnlyDictionary<string, string?> answers, CancellationToken cancellationToken)
    {
        var slug = Answer(answers, "product") ?? Answer(answers, "productSlug");

        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        return await _dbContext.Products
            .Where(p => p.Slug == slug)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid?> DefaultOwnerAsync(CancellationToken cancellationToken)
    {
        var email = _settings.Value("notify.ownerEmail");

        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return await _dbContext.Users
            .Where(u => u.Email == email)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Where the enquiry came from. Never null: "we do not know" and "they typed the address" are
    /// different answers, and a report that conflates them misleads (REQ-LEAD-008).
    /// </summary>
    private static LeadSource SourceOf(IReadOnlyDictionary<string, string> utm, string? referrer)
    {
        if (utm.TryGetValue("utm_medium", out var medium) && !string.IsNullOrWhiteSpace(medium))
        {
            return medium.ToLowerInvariant() switch
            {
                "cpc" or "ppc" or "paid" => LeadSource.Campaign,
                "social" => LeadSource.Social,
                "email" => LeadSource.Email,
                "referral" => LeadSource.Referral,
                "organic" => LeadSource.Organic,
                _ => LeadSource.Campaign,
            };
        }

        if (utm.ContainsKey("utm_source") || utm.ContainsKey("utm_campaign"))
        {
            return LeadSource.Campaign;
        }

        return string.IsNullOrWhiteSpace(referrer) ? LeadSource.Direct : LeadSource.Referral;
    }

    /// <summary>
    /// The reference a visitor is shown and quotes back at us. It is derived from the identifier
    /// rather than being a counter, so nothing has to be locked to produce one and nobody can read
    /// how many enquiries the company gets from two references.
    /// </summary>
    private static string Reference(Guid id) => "ENQ-" + id.ToString("N")[..8].ToUpperInvariant();

    private static string ContentHash(string formKey, string? email, string? phone, string? message)
    {
        // The separator is a character no answer can contain, so "ab" plus "c" and "a" plus
        // "bc" cannot hash the same and be mistaken for one person double-clicking.
        var basis = string.Join(
            '\u001f',
            formKey,
            (email ?? string.Empty).Trim().ToLowerInvariant(),
            (phone ?? string.Empty).Trim(),
            (message ?? string.Empty).Trim());

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(basis))).ToLowerInvariant();
    }

    private static string? Answer(IReadOnlyDictionary<string, string?> answers, string name)
    {
        if (!answers.TryGetValue(name, out var value))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        var dot = value.LastIndexOf('.');

        return at > 0 && dot > at + 1 && dot < value.Length - 1 && !value.Contains(' ', StringComparison.Ordinal);
    }

    /// <summary>
    /// Ten digits starting 6 to 9, which is what an Indian mobile number is. A country code and the
    /// spaces people type are stripped first, because refusing "+91 98765 43210" would be refusing a
    /// correctly written number (A-05).
    /// </summary>
    private static bool LooksLikeIndianMobile(string value)
    {
        var digits = new string([.. value.Where(char.IsDigit)]);

        if (digits.StartsWith("91", StringComparison.Ordinal) && digits.Length == 12)
        {
            digits = digits[2..];
        }

        return digits.Length == 10 && digits[0] is >= '6' and <= '9';
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Lead {reference} created from {email} via the {formKey} form.")]
    private static partial void LogLeadCreated(ILogger logger, string reference, string email, string formKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "A submission from {ipAddress} was rate limited; it may retry in {retryAfterSeconds}s.")]
    private static partial void LogRateLimited(ILogger logger, string ipAddress, int retryAfterSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "A submission from {ipAddress} failed the captcha check: {reason}")]
    private static partial void LogCaptchaRefused(ILogger logger, string ipAddress, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "A submission from {ipAddress} filled the honeypot and was stored as spam.")]
    private static partial void LogHoneypotTripped(ILogger logger, string ipAddress);
}
