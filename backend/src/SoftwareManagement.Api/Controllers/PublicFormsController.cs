using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Leads;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Domain.Leads;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// The public forms: what to render, and where a submission goes.
///
/// This is the only writable endpoint an anonymous caller has, so it is the one that has to survive
/// a flood, a bot and a double-click. Every one of those is handled by the intake service; this
/// controller's job is to gather the network facts honestly and to translate an outcome into a
/// status code without saying more than it should.
/// </summary>
[ApiController]
[Route("api/v1/public/forms")]
[AllowAnonymous]
public sealed class PublicFormsController(
    AppDbContext dbContext,
    ILeadIntakeService intake) : ControllerBase
{
    /// <summary>
    /// The hidden field. It is named after something a form-filling bot expects to find, and no
    /// person ever sees it, so anything in it came from a machine (BR-LEAD-03).
    /// </summary>
    public const string HoneypotField = "website";

    private static readonly string[] UtmKeys =
        ["utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content"];

    private readonly AppDbContext _dbContext = dbContext;
    private readonly ILeadIntakeService _intake = intake;

    /// <summary>
    /// What one form asks. The client renders from this rather than from its own copy, so adding a
    /// question is a data change and the required flags cannot drift apart (REQ-LEAD-002).
    /// </summary>
    [HttpGet("{key}")]
    public async Task<ActionResult<PublicForm>> Get(string key, CancellationToken cancellationToken)
    {
        var form = await _dbContext.FormDefinitions
            .AsNoTracking()
            .Include(f => f.Fields)
            .FirstOrDefaultAsync(f => f.Key == key, cancellationToken).ConfigureAwait(false);

        if (form is null)
        {
            return NotFound();
        }

        var products = form.Fields.Any(f => f.FieldType == FormFieldType.ProductPicker)
            ? await _dbContext.Products
                .AsNoTracking()
                .Where(p => p.Status == ContentStatus.Published || p.Status == ContentStatus.Modified)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
                .Select(p => new ProductChoice(p.Slug, p.Name))
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];

        return Ok(new PublicForm(
            form.Key,
            form.Title,
            form.Intro,
            form.SubmitLabel,
            form.ConsentText,
            form.ConsentVersion,
            form.IsEnabled,
            HoneypotField,
            [.. form.Fields.OrderBy(f => f.SortOrder).Select(f => new PublicFormField(
                f.Name,
                f.Label,
                f.FieldType.ToString(),
                f.IsRequired,
                f.MaxLength,
                f.OptionsJson is null ? [] : JsonSerializer.Deserialize<string[]>(f.OptionsJson) ?? []))],
            products));
    }

    [HttpPost("{key}/submit")]
    public async Task<IActionResult> Submit(string key, SubmitBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var answers = new Dictionary<string, string?>(body.Answers ?? [], StringComparer.Ordinal);

        // The honeypot never reaches the answers: it is not one of the form's questions, and storing
        // it beside real answers would confuse anyone reading a submission later.
        answers.Remove(HoneypotField);

        var request = new IntakeRequest(
            key,
            answers,
            body.Consent,
            body.CaptchaToken,
            body.Honeypot ?? Read(body.Answers, HoneypotField),
            ClientAddress(),
            Request.Headers.UserAgent.ToString(),
            Request.Headers.Referer.ToString(),
            CampaignParameters(body.Utm));

        var result = await _intake.SubmitAsync(request, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            // A real enquiry, a double-click and a bot all get the same answer. The bot learns
            // nothing; the person who double-clicked sees the reference they already have.
            IntakeOutcome.Accepted or IntakeOutcome.Duplicate or IntakeOutcome.SilentlyDiscarded =>
                Accepted(new SubmitResponse(result.Reference!, result.Message!)),

            IntakeOutcome.FormNotFound => NotFound(),

            IntakeOutcome.RateLimited => RateLimited(result),

            IntakeOutcome.CaptchaInvalid => FormProblem(
                StatusCodes.Status400BadRequest, "CAPTCHA_INVALID", "We could not confirm you are a person", result),

            IntakeOutcome.ConsentRequired => FormProblem(
                StatusCodes.Status422UnprocessableEntity, "CONSENT_REQUIRED", "Consent is needed", result),

            IntakeOutcome.FormClosed => FormProblem(
                StatusCodes.Status422UnprocessableEntity, "FORM_CLOSED", "This form is closed", result),

            _ => FormProblem(
                StatusCodes.Status422UnprocessableEntity, "INVALID_SUBMISSION", "Something is missing", result),
        };
    }

    private ObjectResult RateLimited(IntakeResult result)
    {
        // Retry-After is the whole point of a 429: without it the client has to guess, and it will
        // guess wrong in the direction that makes the flood worse (BR-LEAD-04).
        Response.Headers.RetryAfter = (result.RetryAfterSeconds ?? 60).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return FormProblem(StatusCodes.Status429TooManyRequests, "RATE_LIMITED", "Too many enquiries from one place", result);
    }

    private ObjectResult FormProblem(int status, string code, string title, IntakeResult result)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code };

        if (result.Field is not null)
        {
            extensions["field"] = result.Field;
        }

        if (result.RetryAfterSeconds is { } seconds)
        {
            extensions["retryAfterSeconds"] = seconds;
        }

        return Problem(
            title: title,
            detail: result.Message,
            statusCode: status,
            type: "https://softwaremanagement.example/errors/" + code.ToLowerInvariant().Replace('_', '-'),
            extensions: extensions);
    }

    /// <summary>
    /// The address the request came from, preferring the forwarded header when the site sits behind
    /// a proxy. Only the first entry is taken: the rest are appended by intermediaries and are not
    /// evidence of anything.
    /// </summary>
    private string ClientAddress()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].ToString();

        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',', StringSplitOptions.TrimEntries)[0];
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static Dictionary<string, string> CampaignParameters(Dictionary<string, string>? supplied)
    {
        var utm = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in UtmKeys)
        {
            if (supplied is not null && supplied.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                utm[key] = value.Length > 200 ? value[..200] : value;
            }
        }

        return utm;
    }

    private static string? Read(Dictionary<string, string?>? answers, string name) =>
        answers is not null && answers.TryGetValue(name, out var value) ? value : null;
}

public sealed record SubmitBody(
    Dictionary<string, string?>? Answers,
    bool Consent,
    string? CaptchaToken,
    string? Honeypot,
    Dictionary<string, string>? Utm);

public sealed record SubmitResponse(string Reference, string Message);

public sealed record ProductChoice(string Slug, string Name);

public sealed record PublicFormField(
    string Name,
    string Label,
    string FieldType,
    bool IsRequired,
    int? MaxLength,
    IReadOnlyList<string> Options);

public sealed record PublicForm(
    string Key,
    string Title,
    string? Intro,
    string SubmitLabel,
    string ConsentText,
    int ConsentVersion,
    bool IsEnabled,
    string HoneypotField,
    IReadOnlyList<PublicFormField> Fields,
    IReadOnlyList<ProductChoice> Products);
