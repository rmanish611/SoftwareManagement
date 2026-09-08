using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SoftwareManagement.Domain.Identity;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Content;

/// <summary>
/// The API against its own content database, seeded with one user per role so authorization can be
/// asserted alongside behaviour.
/// </summary>
public sealed class ContentFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DatabaseName = "SoftwareManagementDb_Content";

    public const string ConnectionString =
        "Server=.\\SQLEXPRESS;Database=" + DatabaseName + ";Trusted_Connection=True;TrustServerCertificate=True";

    public const string OwnerEmail = "owner@softwaremanagement.test";
    public const string EditorEmail = "editor@softwaremanagement.test";
    public const string SalesEmail = "sales@softwaremanagement.test";
    public const string AuditorEmail = "auditor@softwaremanagement.test";
    public const string Password = "Fixture-Pass-2026";

    public ContentFixture()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", ApiFactory.TestSigningKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", ApiFactory.TestIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", ApiFactory.TestAudience);
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "true");
        Environment.SetEnvironmentVariable("Seed__OwnerEmail", OwnerEmail);
        Environment.SetEnvironmentVariable("Seed__OwnerPassword", Password);
        Environment.SetEnvironmentVariable("Media__RootPath", Path.Combine(Path.GetTempPath(), "sm-media-tests"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(Environments.Production);
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        await EnsureAsync(users, EditorEmail, "Content Editor", RoleNames.Editor);
        await EnsureAsync(users, SalesEmail, "Sales Person", RoleNames.Sales);
        await EnsureAsync(users, AuditorEmail, "Read Only Auditor", RoleNames.Auditor);

        await db.LoginAttempts.ExecuteDeleteAsync();
    }

    public new Task DisposeAsync() => Task.CompletedTask;

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

    public async Task<HttpClient> ClientAsAsync(string email)
    {
        using var anonymous = CreateClient();
        var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = Password, twoFactorCode = (string?)null });

        if (!login.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Sign-in for {email} returned {(int)login.StatusCode}.");
        }

        var body = await login.Content.ReadFromJsonAsync<IdentityFixtureLogin>();
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", body!.AccessToken);
        return client;
    }

    /// <summary>Creates a draft page and returns its id, for tests that start from one.</summary>
    public static async Task<Guid> CreateDraftPageAsync(HttpClient client, string title, string? slug = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/admin/pages", new
        {
            title,
            slug,
            pageType = "Custom",
            body = "<p>Body</p>",
            metaTitle = title,
            metaDescription = "A description that sits comfortably inside the guidance range for length.",
        });

        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<PageRow>();
        return page!.Id;
    }

    public sealed record IdentityFixtureLogin(string AccessToken);

    public sealed record PageRow(Guid Id, string Title, string Slug, string PageType, string Status, string? Body, string? MetaTitle);
}

[CollectionDefinition(Name)]
public sealed class ContentTestGroup : ICollectionFixture<ContentFixture>
{
    public const string Name = "content";
}
