using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Domain.Identity;
using SoftwareManagement.Domain.Portfolio;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Portfolio;

/// <summary>
/// The API against its own portfolio database.
///
/// A database of its own for the same reason the others have one: several of these rules are about
/// what a *list* contains - which industries exist, which APIs are published - and a list shared
/// with another suite's writes is not one anybody can assert on.
/// </summary>
public sealed class PortfolioFixture : ConfiguredApiFactory, IAsyncLifetime
{
    public const string DatabaseName = "SoftwareManagementDb_Portfolio";

    public const string ConnectionString =
        "Server=.\\SQLEXPRESS;Database=" + DatabaseName + ";Trusted_Connection=True;TrustServerCertificate=True";

    public const string OwnerEmail = "owner@softwaremanagement.test";
    public const string EditorEmail = "editor@softwaremanagement.test";
    public const string SalesEmail = "sales@softwaremanagement.test";
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
            ["Billing__SweepEnabled"] = "false",
            ["Media__RootPath"] = Path.Combine(Path.GetTempPath(), "sm-media-tests"),
        };

    /// <summary>A media asset for client logos to point at, since the column is required.</summary>
    public Guid LogoAssetId { get; private set; }

    /// <summary>A published product for the two cross-linking rules to point at.</summary>
    public Guid ProductId { get; private set; }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        await EnsureAsync(users, EditorEmail, "Content Editor", RoleNames.Editor);
        await EnsureAsync(users, SalesEmail, "Sales Person", RoleNames.Sales);

        await ClearPortfolioAsync(db);
        LogoAssetId = await EnsureAssetAsync(db);
        ProductId = await EnsureProductAsync(db);
    }

    private static async Task ClearPortfolioAsync(AppDbContext db)
    {
        // Children first: a case study hangs off a project, a version off an API entry, and a
        // project points at a logo.
        await db.CaseStudies.ExecuteDeleteAsync();
        await db.ProjectTechnologies.ExecuteDeleteAsync();
        await db.Projects.ExecuteDeleteAsync();
        await db.ClientLogos.ExecuteDeleteAsync();
        await db.ApiVersions.ExecuteDeleteAsync();
        await db.ApiCatalogEntries.ExecuteDeleteAsync();
    }

    private static async Task<Guid> EnsureAssetAsync(AppDbContext db)
    {
        var existing = await db.MediaAssets.AsNoTracking()
            .FirstOrDefaultAsync(a => a.FileName == "portfolio-fixture-logo.png");

        if (existing is not null)
        {
            return existing.Id;
        }

        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            FileName = "portfolio-fixture-logo.png",
            StorageKey = "fixtures/portfolio-fixture-logo.png",
            ContentType = "image/png",
            SizeBytes = 68,
            AltText = "A fixture logo",
            CreatedBy = "test-fixture",
        };

        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
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

    /// <summary>A client logo, with or without the client's agreement to be named.</summary>
    public async Task<Guid> AddLogoAsync(string displayName, bool hasPermission, int sortOrder = 1)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var logo = new ClientLogo
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            MediaAssetId = LogoAssetId,
            HasPermission = hasPermission,
            SortOrder = sortOrder,
            CreatedBy = "test-fixture",
        };

        db.ClientLogos.Add(logo);
        await db.SaveChangesAsync();
        return logo.Id;
    }

    /// <summary>A testimonial, with the three things BR-PRJ-03 asks for or without them.</summary>
    public async Task<Guid> AddTestimonialAsync(string nonce, bool hasPermission, bool published = true, string role = "Operations Director")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var testimonial = new Testimonial
        {
            Id = Guid.NewGuid(),
            AuthorName = $"Priya Sharma {nonce}",
            AuthorRole = role,
            OrganisationName = $"Northwind {nonce}",
            Quote = "They understood the problem before writing anything.",
            HasPermission = hasPermission,
            IsPublished = published,
            SortOrder = 1,
            CreatedBy = "test-fixture",
        };

        db.Testimonials.Add(testimonial);
        await db.SaveChangesAsync();
        return testimonial.Id;
    }

    /// <summary>A technology in the stack list, for the evidence page.</summary>
    public async Task<Guid> AddTechnologyAsync(string name)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Technologies.AsNoTracking().FirstOrDefaultAsync(t => t.Name == name);

        if (existing is not null)
        {
            return existing.Id;
        }

        var technology = new Technology
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = TechnologyCategory.Framework,
            SortOrder = 50,
            CreatedBy = "test-fixture",
        };

        db.Technologies.Add(technology);
        await db.SaveChangesAsync();
        return technology.Id;
    }

    private static async Task<Guid> EnsureProductAsync(AppDbContext db)
    {
        var existing = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == "portfolio-fixture-product");

        if (existing is not null)
        {
            return existing.Id;
        }

        var category = await db.ProductCategories.FirstAsync(c => c.Slug == "erp");

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Portfolio fixture product",
            Slug = "portfolio-fixture-product",
            Tagline = "Something a project and an API can point at.",
            Summary = "Exists so the cross-linking rules do not depend on the catalogue tests having run.",
            CategoryId = category.Id,
            Status = ContentStatus.Published,
            PublishedAtUtc = DateTime.UtcNow,
            CreatedBy = "test-fixture",
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private sealed record LoginRow(string AccessToken);
}

[CollectionDefinition(Name)]
public sealed class PortfolioTestGroup : ICollectionFixture<PortfolioFixture>
{
    public const string Name = "portfolio";
}
