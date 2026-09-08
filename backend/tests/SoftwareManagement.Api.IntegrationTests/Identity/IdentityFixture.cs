using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Json;
using SoftwareManagement.Domain.Identity;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Identity;

/// <summary>
/// Hosts the real API against a dedicated identity test database, migrated and seeded with one
/// user per role. Passwords here are test fixtures, not secrets: the database is created and
/// dropped by the test run and grants access to nothing.
/// </summary>
public sealed class IdentityFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DatabaseName = "SoftwareManagementDb_Identity";

    public const string ConnectionString =
        "Server=.\\SQLEXPRESS;Database=" + DatabaseName + ";Trusted_Connection=True;TrustServerCertificate=True";

    public const string OwnerEmail = "owner@softwaremanagement.test";
    public const string SalesEmail = "sales@softwaremanagement.test";
    public const string EditorEmail = "editor@softwaremanagement.test";
    public const string AuditorEmail = "auditor@softwaremanagement.test";

    /// <summary>Meets the 12-character, three-character-class policy in BR-IAM-01.</summary>
    public const string GoodPassword = "Fixture-Pass-2026";

    public const string WrongPassword = "Definitely-Wrong-2026";

    public IdentityFixture()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", ApiFactory.TestSigningKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", ApiFactory.TestIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", ApiFactory.TestAudience);
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "true");
        Environment.SetEnvironmentVariable("Seed__OwnerEmail", OwnerEmail);
        Environment.SetEnvironmentVariable("Seed__OwnerPassword", GoodPassword);
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

        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        await EnsureUserAsync(users, SalesEmail, "Sales Person", RoleNames.Sales);
        await EnsureUserAsync(users, EditorEmail, "Content Editor", RoleNames.Editor);
        await EnsureUserAsync(users, AuditorEmail, "Auditor", RoleNames.Auditor);

        // The lockout counter reads the LoginAttempts table, so a previous run's failures would
        // lock the fixture out of its own accounts. Start every run from a clean sheet.
        await db.LoginAttempts.ExecuteDeleteAsync();
        await db.RefreshTokens.ExecuteDeleteAsync();
    }

    public new Task DisposeAsync() => Task.CompletedTask;

    private static async Task EnsureUserAsync(UserManager<AdminUser> users, string email, string fullName, string role)
    {
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            return;
        }

        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            IsActive = true,
            CreatedBy = "test-fixture",
        };

        var created = await users.CreateAsync(user, GoodPassword);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the {role} fixture user: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(user, role);
    }

    /// <summary>Signs in and returns the access token, failing the test loudly if sign-in fails.</summary>
    public async Task<string> SignInAsync(string email, string password = GoodPassword)
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, twoFactorCode = (string?)null });

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Sign-in for {email} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        var body = await response.Content.ReadFromJsonAsync<LoginResponseBody>();
        return body?.AccessToken ?? throw new InvalidOperationException("Sign-in returned no access token.");
    }

    public async Task<HttpClient> SignedInClientAsync(string email)
    {
        var token = await SignInAsync(email);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    public sealed record LoginResponseBody(
        string AccessToken,
        DateTime ExpiresAtUtc,
        string FullName,
        string Email,
        string[] Roles,
        string[] Permissions);
}
