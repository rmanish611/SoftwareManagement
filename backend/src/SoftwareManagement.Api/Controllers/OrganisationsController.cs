using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Crm;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// Customer companies and the people at them.
///
/// The GSTIN is checked here rather than trusted, because a wrong one is caught while the person
/// still has the certificate in front of them instead of weeks later when an invoice is refused
/// (BR-CUST-01).
/// </summary>
[ApiController]
[Route("api/v1/organisations")]
public sealed class OrganisationsController(AppDbContext dbContext) : ControllerBase
{
    private const int MaxPageSize = 100;

    private const string CsvHeader = "DisplayName,LegalName,Gstin,City,State,PostalCode,Country,Status,ContactCount";

    /// <summary>
    /// CRLF, written out rather than taken from the environment. RFC 4180 says CRLF, and a file
    /// produced on Linux with bare LF is read by some spreadsheet importers as one long row - so
    /// the export would differ depending on where the server happens to run.
    /// </summary>
    private const string Newline = "\r\n";

    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet]
    [Authorize(Policy = Permissions.Crm.OrganisationRead)]
    public async Task<ActionResult<IReadOnlyList<OrganisationSummary>>> List(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = _dbContext.Organisations.AsNoTracking();

        if (Enum.TryParse<OrganisationStatus>(status, ignoreCase: true, out var wanted))
        {
            query = query.Where(o => o.Status == wanted);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o =>
                EF.Functions.Like(o.DisplayName, $"%{term}%")
                || EF.Functions.Like(o.LegalName, $"%{term}%")
                || (o.Gstin != null && EF.Functions.Like(o.Gstin, $"%{term}%")));
        }

        var organisations = await query
            .OrderBy(o => o.DisplayName)
            .Take(take)
            .Select(o => new OrganisationSummary(
                o.Id,
                o.LegalName,
                o.DisplayName,
                o.Gstin,
                o.City,
                o.State,
                o.Status.ToString(),
                o.Contacts.Count))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(organisations);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Crm.OrganisationRead)]
    public async Task<ActionResult<OrganisationDetail>> Get(Guid id, CancellationToken cancellationToken)
    {
        var organisation = await _dbContext.Organisations
            .AsNoTracking()
            .Include(o => o.Contacts)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken).ConfigureAwait(false);

        if (organisation is null)
        {
            return NotFound();
        }

        return Ok(new OrganisationDetail(
            organisation.Id,
            organisation.LegalName,
            organisation.DisplayName,
            organisation.Gstin,
            organisation.Website,
            organisation.Industry,
            organisation.AddressLine1,
            organisation.AddressLine2,
            organisation.City,
            organisation.State,
            organisation.PostalCode,
            organisation.Country,
            organisation.Status.ToString(),
            [.. organisation.Contacts
                .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.FullName)
                .Select(c => new ContactRow(c.Id, c.FullName, c.Email, c.Phone, c.JobTitle, c.IsPrimary, c.IsActive))]));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Crm.OrganisationWrite)]
    public async Task<ActionResult<OrganisationSummary>> Create(OrganisationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body.LegalName))
        {
            return Refuse("NAME_REQUIRED", "legalName", "A company needs a name.");
        }

        var gstin = Gstin.Normalise(body.Gstin);

        if (gstin is not null && !Gstin.IsValid(gstin))
        {
            return Refuse("GSTIN_INVALID", "gstin", Gstin.Explanation);
        }

        if (!string.IsNullOrWhiteSpace(body.PostalCode) && body.PostalCode.Trim().Length is < 4 or > 12)
        {
            // An invoice with a three-character postal code is an invoice that does not arrive
            // (REQ-CUST-005).
            return Refuse("POSTAL_CODE_INVALID", "postalCode", "A postal code is between 4 and 12 characters.");
        }

        if (gstin is not null)
        {
            var holder = await _dbContext.Organisations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Gstin == gstin && o.Status != OrganisationStatus.Inactive, cancellationToken)
                .ConfigureAwait(false);

            if (holder is not null)
            {
                // Named, not merely refused: the person is almost always looking at the company
                // they already created last month (REQ-CUST-002).
                var extensions = Extensions("GSTIN_DUPLICATE", "gstin");
                extensions["existingOrganisationId"] = holder.Id;
                extensions["existingOrganisationName"] = holder.DisplayName;

                return Problem(
                    title: "That GSTIN is already on file",
                    detail: $"{holder.DisplayName} already uses that number.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: ErrorType("GSTIN_DUPLICATE"),
                    extensions: extensions);
            }
        }

        var organisation = new Organisation
        {
            Id = Guid.NewGuid(),
            LegalName = body.LegalName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(body.DisplayName) ? body.LegalName.Trim() : body.DisplayName.Trim(),
            Gstin = gstin,
            Website = body.Website,
            Industry = body.Industry,
            AddressLine1 = body.AddressLine1,
            AddressLine2 = body.AddressLine2,
            City = body.City,
            State = body.State,
            PostalCode = body.PostalCode,

            // Two letters, upper case, because that is what a country code is and "in" is the same
            // country as "IN".
            Country = string.IsNullOrWhiteSpace(body.Country) ? "IN" : body.Country.Trim().ToUpperInvariant(),

            // Prospect until somebody buys something (REQ-CUST-001).
            Status = OrganisationStatus.Prospect,
            CreatedBy = Actor(),
        };

        _dbContext.Organisations.Add(organisation);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Created(
            $"/api/v1/organisations/{organisation.Id}",
            new OrganisationSummary(
                organisation.Id,
                organisation.LegalName,
                organisation.DisplayName,
                organisation.Gstin,
                organisation.City,
                organisation.State,
                organisation.Status.ToString(),
                0));
    }

    [HttpPost("{id:guid}/contacts")]
    [Authorize(Policy = Permissions.Crm.ContactWrite)]
    public async Task<ActionResult<ContactRow>> AddContact(Guid id, ContactBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var organisation = await _dbContext.Organisations
            .Include(o => o.Contacts)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken).ConfigureAwait(false);

        if (organisation is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(body.Email))
        {
            return Refuse("EMAIL_REQUIRED", "email", "A contact needs somewhere to write to.");
        }

        var email = body.Email.Trim();

        // One email per company, though the same person may appear at two companies - people change
        // jobs (BR-CUST-02).
        if (organisation.Contacts.Any(c => string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            return Problem(
                title: "That person is already here",
                detail: $"{email} is already a contact at {organisation.DisplayName}.",
                statusCode: StatusCodes.Status409Conflict,
                type: ErrorType("CONTACT_EXISTS"),
                extensions: Extensions("CONTACT_EXISTS", "email"));
        }

        var contact = new Contact
        {
            Id = Guid.NewGuid(),
            OrganisationId = organisation.Id,
            FullName = string.IsNullOrWhiteSpace(body.FullName) ? email : body.FullName.Trim(),
            Email = email,
            Phone = body.Phone,
            JobTitle = body.JobTitle,
            IsPrimary = body.IsPrimary || organisation.Contacts.Count == 0,
            CreatedBy = Actor(),
        };

        if (contact.IsPrimary)
        {
            // Exactly one person to ring. Two primaries is the same as none.
            foreach (var other in organisation.Contacts)
            {
                other.IsPrimary = false;
                other.ModifiedBy = Actor();
            }
        }

        _dbContext.Contacts.Add(contact);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Created(
            $"/api/v1/organisations/{organisation.Id}",
            new ContactRow(contact.Id, contact.FullName, contact.Email, contact.Phone, contact.JobTitle, contact.IsPrimary, contact.IsActive));
    }

    /// <summary>
    /// Retiring a company rather than removing it. A company a quote or an invoice points at cannot
    /// be deleted, because the document would then reference nothing (BR-CUST-03).
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Permissions.Crm.OrganisationWrite)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var organisation = await _dbContext.Organisations
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken).ConfigureAwait(false);

        if (organisation is null)
        {
            return NotFound();
        }

        organisation.Status = OrganisationStatus.Inactive;
        organisation.ModifiedBy = Actor();

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Refuses to delete, always, and says what to do instead.
    ///
    /// A company a quote or an invoice points at cannot be removed without the document coming to
    /// reference nothing, and the answer for one that is simply finished with is to retire it. The
    /// route exists so the refusal is explicit rather than a 404 somebody reads as "already gone"
    /// (BR-CUST-03, REQ-CUST-006).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Crm.OrganisationWrite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var organisation = await _dbContext.Organisations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken).ConfigureAwait(false);

        if (organisation is null)
        {
            return NotFound();
        }

        var quotes = await _dbContext.Quotes
            .CountAsync(q => q.OrganisationId == id, cancellationToken).ConfigureAwait(false);

        var extensions = Extensions("ORGANISATION_IN_USE", null);
        extensions["quoteCount"] = quotes;

        return Problem(
            title: "A customer is retired, not removed",
            detail: quotes > 0
                ? $"{quotes} quote(s) reference this organisation. Set it inactive instead."
                : "History is kept. Set it inactive instead.",
            statusCode: StatusCodes.Status409Conflict,
            type: ErrorType("ORGANISATION_IN_USE"),
            extensions: extensions);
    }

    /// <summary>
    /// The customer list as a CSV, with the same columns as the screen so the two can be reconciled
    /// (REQ-CUST-008).
    /// </summary>
    [HttpGet("export")]
    [Authorize(Policy = Permissions.Reporting.Export)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Organisations
            .AsNoTracking()
            .OrderBy(o => o.DisplayName)
            .Select(o => new
            {
                o.DisplayName,
                o.LegalName,
                o.Gstin,
                o.City,
                o.State,
                o.PostalCode,
                o.Country,
                o.Status,
                Contacts = o.Contacts.Count,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var csv = new System.Text.StringBuilder();
        csv.Append(CsvHeader).Append(Newline);

        foreach (var row in rows)
        {
            csv.Append(string.Join(',', new[]
            {
                Csv(row.DisplayName),
                Csv(row.LegalName),
                Csv(row.Gstin),
                Csv(row.City),
                Csv(row.State),
                Csv(row.PostalCode),
                Csv(row.Country),
                Csv(row.Status.ToString()),
                row.Contacts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }) + Newline);
        }

        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "customers.csv");
    }

    /// <summary>
    /// One CSV field. Quoted when it contains a comma, a quote or a newline, with inner quotes
    /// doubled - a company name with a comma in it is ordinary, and splitting the file on commas
    /// afterwards is what goes wrong.
    /// </summary>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    /// <summary>
    /// Companies that look like this one, by normalised name or by shared email domain. Surfaced,
    /// never merged (BR-CUST-04).
    /// </summary>
    [HttpGet("{id:guid}/duplicates")]
    [Authorize(Policy = Permissions.Crm.OrganisationRead)]
    public async Task<ActionResult<IReadOnlyList<OrganisationMatch>>> Duplicates(Guid id, CancellationToken cancellationToken)
    {
        var organisation = await _dbContext.Organisations
            .AsNoTracking()
            .Include(o => o.Contacts)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken).ConfigureAwait(false);

        if (organisation is null)
        {
            return NotFound();
        }

        var normalised = Normalise(organisation.DisplayName);
        var domains = organisation.Contacts
            .Select(c => DomainOf(c.Email))
            .Where(d => d is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidates = await _dbContext.Organisations
            .AsNoTracking()
            .Where(o => o.Id != id)
            .Select(o => new
            {
                o.Id,
                o.DisplayName,
                o.Gstin,
                Status = o.Status,
                Emails = o.Contacts.Select(c => c.Email).ToList(),
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var matches = candidates
            .Select(c => new
            {
                c.Id,
                c.DisplayName,
                c.Gstin,
                c.Status,
                Reason = Why(normalised, domains, Normalise(c.DisplayName), c.Emails),
            })
            .Where(c => c.Reason is not null)
            .Take(10)
            .Select(c => new OrganisationMatch(c.Id, c.DisplayName, c.Gstin, c.Status.ToString(), c.Reason!))
            .ToList();

        return Ok(matches);
    }

    /// <summary>
    /// Case, punctuation and the suffixes every Indian company name ends in removed, so "Acme
    /// Technologies Pvt. Ltd." and "ACME Technologies Private Limited" compare equal.
    /// </summary>
    private static string Normalise(string name)
    {
        var lowered = new string([.. name.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ')]);

        foreach (var suffix in new[] { "private limited", "pvt ltd", "pvt limited", "limited", "ltd", "llp", "inc" })
        {
            if (lowered.EndsWith(suffix, StringComparison.Ordinal))
            {
                lowered = lowered[..^suffix.Length];
            }
        }

        return string.Join(' ', lowered.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? DomainOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at > 0 && at < email.Length - 1 ? email[(at + 1)..] : null;
    }

    private static string? Why(string normalised, List<string?> domains, string otherNormalised, List<string> otherEmails)
    {
        if (normalised.Length > 0 && string.Equals(normalised, otherNormalised, StringComparison.Ordinal))
        {
            return "The same company name, once the suffixes are ignored.";
        }

        var shared = otherEmails
            .Select(DomainOf)
            .FirstOrDefault(d => d is not null && domains.Contains(d, StringComparer.OrdinalIgnoreCase));

        return shared is null ? null : $"Contacts at {shared}.";
    }

    private ObjectResult Refuse(string code, string field, string message) => Problem(
        title: "That will not do",
        detail: message,
        statusCode: StatusCodes.Status422UnprocessableEntity,
        type: ErrorType(code),
        extensions: Extensions(code, field));

    private static string ErrorType(string code) =>
        "https://softwaremanagement.example/errors/" + code.ToLowerInvariant().Replace('_', '-');

    private static Dictionary<string, object?> Extensions(string code, string? field)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code };

        if (field is not null)
        {
            extensions["field"] = field;
        }

        if (string.Equals(code, "GSTIN_INVALID", StringComparison.Ordinal))
        {
            // The pattern travels with the refusal so the form can show what was expected rather
            // than repeating the rule in two places (REQ-CUST-001).
            extensions["expectedPattern"] = Gstin.Pattern;
        }

        return extensions;
    }

    private string Actor() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

public sealed record OrganisationBody(
    string LegalName,
    string? DisplayName,
    string? Gstin,
    string? Website,
    string? Industry,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country);

public sealed record ContactBody(string? FullName, string Email, string? Phone, string? JobTitle, bool IsPrimary);

public sealed record OrganisationSummary(
    Guid Id,
    string LegalName,
    string DisplayName,
    string? Gstin,
    string? City,
    string? State,
    string Status,
    int ContactCount);

public sealed record ContactRow(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string? JobTitle,
    bool IsPrimary,
    bool IsActive);

public sealed record OrganisationDetail(
    Guid Id,
    string LegalName,
    string DisplayName,
    string? Gstin,
    string? Website,
    string? Industry,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string Country,
    string Status,
    IReadOnlyList<ContactRow> Contacts);

public sealed record OrganisationMatch(Guid Id, string DisplayName, string? Gstin, string Status, string Reason);
