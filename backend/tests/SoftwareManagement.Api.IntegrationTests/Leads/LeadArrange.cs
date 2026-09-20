using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Leads;

/// <summary>
/// Arrangement for the lead tests.
///
/// Every test gets its own apparent address and its own captcha token, derived from one nonce, so
/// the rate limiter and the replay check cannot make one test fail because of another.
/// </summary>
public static class LeadArrange
{
    public static string Nonce() => Guid.NewGuid().ToString("N")[..8];

    private static int _lastOctet;

    private static readonly ConcurrentDictionary<string, string> AddressesByNonce =
        new(StringComparer.Ordinal);

    /// <summary>
    /// A distinct address per test, from the documentation range reserved for examples, so nothing
    /// here could ever be mistaken for a real one.
    ///
    /// The address is handed out by a counter rather than derived from the nonce's hash. A hash of
    /// twenty nonces into two hundred addresses collides more often than not, and two tests sharing
    /// an address share the rate limiter's allowance of five - so one of them is refused with a 429
    /// that has nothing to do with what it was asserting. String hashes are also seeded per process
    /// in .NET, which is why the failure used to land on a different test every run.
    ///
    /// The same nonce always gets the same address, because a test asserts on the address it was
    /// given after the fact.
    /// </summary>
    public static string Address(string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        return AddressesByNonce.GetOrAdd(
            nonce,
            _ => $"198.51.100.{Interlocked.Increment(ref _lastOctet) % 250 + 1}");
    }

    /// <summary>
    /// A token this fixture's configured bypass will accept. The bypass is a prefix, so each nonce
    /// yields a distinct token: a token is spendable once, bypass or not, so tests that need a
    /// second submission need a second token, and the one test about replay reuses the same string
    /// deliberately.
    /// </summary>
    public static string Token(string nonce) => $"{LeadFixture.CaptchaBypass}-{nonce}";

    public static SubmitRequest ContactBody(string nonce) => new()
    {
        Answers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["fullName"] = $"PROBE-{nonce}",
            ["email"] = $"probe-{nonce}@example.test",
            ["message"] = $"An enquiry about the system, reference {nonce}.",
        },
        Consent = true,
        CaptchaToken = Token(nonce),
    };

    /// <summary>Publishes a product so a demo request has something real to point at.</summary>
    public static async Task<string> PublishProductAsync(LeadFixture fixture, string nonce)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = await db.ProductCategories.FirstAsync(c => c.Slug == "erp");

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = $"Probe product {nonce}",
            Slug = $"probe-product-{nonce}",
            Tagline = "A product for the lead tests to point at.",
            Summary = "Published so a demo request can name it.",
            CategoryId = category.Id,
            Status = ContentStatus.Published,
            PublishedAtUtc = DateTime.UtcNow,
            CreatedBy = "test-fixture",
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Slug;
    }

    /// <summary>
    /// The body the public form endpoint accepts. It is a class rather than a record because tests
    /// change one field of an otherwise valid body, which is how they stay readable: the difference
    /// between the test and the happy path is the one line that changes.
    /// </summary>
    public sealed class SubmitRequest
    {
        public Dictionary<string, string?> Answers { get; set; } = new(StringComparer.Ordinal);

        public bool Consent { get; set; }

        public string? CaptchaToken { get; set; }

        public string? Honeypot { get; set; }

        public Dictionary<string, string>? Utm { get; set; }
    }

    public sealed record SubmitRow(string Reference, string Message);

    public sealed record ProblemRow(string? Title, string? Detail, int? Status, string? Code, string? Field, int? RetryAfterSeconds);

    public sealed record FormFieldRow(string Name, string Label, string FieldType, bool IsRequired, int? MaxLength);

    public sealed record FormRow(
        string Key,
        string Title,
        string? Intro,
        string SubmitLabel,
        string ConsentText,
        int ConsentVersion,
        bool IsEnabled,
        string HoneypotField,
        IReadOnlyList<FormFieldRow> Fields,
        IReadOnlyList<ProductChoiceRow> Products);

    public sealed record ProductChoiceRow(string Slug, string Name);

    public sealed record LeadRow(Guid Id, string FullName, string? Email, string Stage, string Source);

    public sealed record ConsentRow(string Text, int Version, string Purpose, DateTime GivenAtUtc);

    /// <summary>
    /// One enquiry as the admin side reads it back. Declared in full rather than as a subset,
    /// because a field the API stops sending would then deserialise as null and the test would
    /// still pass: naming every field is what makes the shape itself part of the assertion.
    /// </summary>
    public sealed record LeadDetailRow(
        Guid Id,
        string FullName,
        string? Email,
        string? Phone,
        string? CompanyName,
        string? Message,
        string Stage,
        string Source,
        string? UtmJson,
        string? Product,
        DateTime CreatedAtUtc,
        DateTime SlaDueAtUtc,
        ConsentRow? Consent);
}
