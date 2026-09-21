using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Domain.Crm;
using SoftwareManagement.Domain.Identity;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Sales;

/// <summary>
/// The API against its own sales database.
///
/// Quoting gets a database of its own for the same reason lead capture did: the rule that matters
/// most here is about numbers being contiguous, and a sequence shared with another suite's writes
/// is not a sequence anybody can assert on.
/// </summary>
public sealed class SalesFixture : ConfiguredApiFactory, IAsyncLifetime
{
    public const string DatabaseName = "SoftwareManagementDb_Sales";

    public const string ConnectionString =
        "Server=.\\SQLEXPRESS;Database=" + DatabaseName + ";Trusted_Connection=True;TrustServerCertificate=True";

    public const string OwnerEmail = "owner@softwaremanagement.test";
    public const string SalesEmail = "sales@softwaremanagement.test";
    public const string EditorEmail = "editor@softwaremanagement.test";
    public const string Password = "Fixture-Pass-2026";

    protected override IReadOnlyDictionary<string, string?> Settings { get; } =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings__Default"] = ConnectionString,
            ["Jwt__Key"] = ApiFactory.TestSigningKey,
            ["Jwt__Issuer"] = ApiFactory.TestIssuer,
            ["Jwt__Audience"] = ApiFactory.TestAudience,
            ["Database__MigrateOnStartup"] = "true",
            ["Seed__OwnerEmail"] = OwnerEmail,
            ["Seed__OwnerPassword"] = Password,
            ["Outbox__PumpEnabled"] = "false",
            ["LeadSweep__Enabled"] = "false",
            ["Media__RootPath"] = Path.Combine(Path.GetTempPath(), "sm-media-tests"),
        };

    /// <summary>A published product for quote lines to point at, created once for the collection.</summary>
    public Guid ProductId { get; private set; }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        await EnsureAsync(users, SalesEmail, "Sales Person", RoleNames.Sales);
        await EnsureAsync(users, EditorEmail, "Content Editor", RoleNames.Editor);

        await ClearSalesDataAsync(db);
        ProductId = await EnsureProductAsync(db);
    }

    /// <summary>
    /// Empties the quotes and the number sequences.
    ///
    /// The sequences go too, deliberately. One test asserts that twenty quotes created at once get
    /// a contiguous block, and "contiguous" can only be checked from a known starting point.
    /// </summary>
    private static async Task ClearSalesDataAsync(AppDbContext db)
    {
        await db.QuoteLineItems.ExecuteDeleteAsync();
        await db.Quotes.ExecuteDeleteAsync();
        await db.NumberSequences.ExecuteDeleteAsync();
        await db.Contacts.ExecuteDeleteAsync();
        await db.Organisations.ExecuteDeleteAsync();
    }

    private static async Task<Guid> EnsureProductAsync(AppDbContext db)
    {
        var existing = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == "quote-fixture-product");

        if (existing is not null)
        {
            return existing.Id;
        }

        var category = await db.ProductCategories.FirstAsync(c => c.Slug == "erp");

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Quote fixture product",
            Slug = "quote-fixture-product",
            Tagline = "Something for a quote line to point at.",
            Summary = "Exists so the sales tests do not depend on the catalogue tests having run.",
            CategoryId = category.Id,
            Status = ContentStatus.Published,
            PublishedAtUtc = DateTime.UtcNow,
            CreatedBy = "test-fixture",
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private static async Task EnsureAsync(UserManager<AdminUser> users, string email, string name, string role)
    {
        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = name,
            IsActive = true,
            CreatedBy = "test-fixture",
        };

        var created = await users.CreateAsync(user, Password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(user, role);
    }

    public new Task DisposeAsync() => Task.CompletedTask;

    public async Task<HttpClient> ClientAsAsync(string email)
    {
        using var anonymous = CreateClient();
        var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = Password, twoFactorCode = (string?)null });

        if (!login.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Sign-in for {email} returned {(int)login.StatusCode}.");
        }

        var body = await login.Content.ReadFromJsonAsync<LoginRow>();
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", body!.AccessToken);
        return client;
    }

    public IServiceScope NewScope() => Services.CreateScope();

    /// <summary>A company with one person at it, which is the least a quote needs.</summary>
    public async Task<(Guid OrganisationId, Guid ContactId)> AddCustomerAsync(string nonce)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var organisation = new Organisation
        {
            Id = Guid.NewGuid(),
            LegalName = $"Probe Industries {nonce} Private Limited",
            DisplayName = $"Probe Industries {nonce}",
            Status = OrganisationStatus.Prospect,
            CreatedBy = "test-fixture",
        };

        var contact = new Contact
        {
            Id = Guid.NewGuid(),
            OrganisationId = organisation.Id,
            FullName = $"Probe Buyer {nonce}",
            Email = $"buyer-{nonce}@probe.test",
            IsPrimary = true,
            CreatedBy = "test-fixture",
        };

        db.Organisations.Add(organisation);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        return (organisation.Id, contact.Id);
    }

    private sealed record LoginRow(string AccessToken);
}

[CollectionDefinition(Name)]
public sealed class SalesTestGroup : ICollectionFixture<SalesFixture>
{
    public const string Name = "sales";
}
