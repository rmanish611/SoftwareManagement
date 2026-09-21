using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SoftwareManagement.Api.IntegrationTests.Content;
using SoftwareManagement.Domain.Identity;
using SoftwareManagement.Domain.Leads;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Leads;

/// <summary>
/// The API against its own lead database.
///
/// Lead capture gets a database of its own because almost every one of its rules is about counting:
/// how many submissions came from this address, whether this exact message arrived before, how many
/// messages are waiting to be sent. Sharing a database with the content tests would make those
/// counts depend on what else happened to run first.
///
/// The outbox pump is switched off. Tests run one pass explicitly, so a timer cannot send a message
/// halfway through an assertion about the queue.
/// </summary>
public sealed class LeadFixture : ConfiguredApiFactory, IAsyncLifetime
{
    public const string DatabaseName = "SoftwareManagementDb_Leads";

    public const string ConnectionString =
        "Server=.\\SQLEXPRESS;Database=" + DatabaseName + ";Trusted_Connection=True;TrustServerCertificate=True";

    public const string OwnerEmail = "owner@softwaremanagement.test";
    public const string SalesEmail = "sales@softwaremanagement.test";
    public const string EditorEmail = "editor@softwaremanagement.test";
    public const string Password = "Fixture-Pass-2026";

    /// <summary>
    /// The token the tests present instead of a real Turnstile challenge. It is only ever honoured
    /// because this fixture sets it; a deployment that does not set it has no bypass at all.
    /// </summary>
    public const string CaptchaBypass = "test-bypass";

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
            ["Captcha__BypassToken"] = CaptchaBypass,
            ["Outbox__PumpEnabled"] = "false",
            ["Media__RootPath"] = Path.Combine(Path.GetTempPath(), "sm-media-tests"),
        };

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        await EnsureAsync(users, SalesEmail, "Sales Person", RoleNames.Sales);
        await EnsureAsync(users, EditorEmail, "Content Editor", RoleNames.Editor);

        await ClearLeadDataAsync(db);
    }

    /// <summary>
    /// Empties everything a previous run submitted, because two of the rules under test count rows
    /// that outlive the run that wrote them.
    ///
    /// The rate limiter deliberately counts submissions in the database rather than in memory, over
    /// ten minutes and over a day (BR-LEAD-04), so yesterday's run is still inside today's window
    /// and a second run within ten minutes would be refused for reasons that have nothing to do
    /// with the test. The outbox is the same story from the other end: one pass takes twenty due
    /// messages, ordered oldest first, so a backlog left behind by an earlier run pushes the
    /// message a test just queued out of reach of the pump.
    ///
    /// Children go first: delivery logs hang off outbox messages, consent records off submissions,
    /// and submissions off leads.
    /// </summary>
    private static async Task ClearLeadDataAsync(AppDbContext db)
    {
        await db.EmailDeliveryLogs.ExecuteDeleteAsync();
        await db.OutboxEmails.ExecuteDeleteAsync();
        await db.ConsentRecords.ExecuteDeleteAsync();
        await db.FormSubmissions.ExecuteDeleteAsync();
        await db.LeadActivities.ExecuteDeleteAsync();

        // A lead points at the customer record it became, so the leads go before the companies do.
        await db.Leads.ExecuteDeleteAsync();
        await db.Contacts.ExecuteDeleteAsync();
        await db.Organisations.ExecuteDeleteAsync();
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

        var body = await login.Content.ReadFromJsonAsync<LoginRow>();
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", body!.AccessToken);
        return client;
    }

    /// <summary>
    /// A client that presents a chosen address, so the rate limiter can be exercised from more than
    /// one apparent origin inside one test run.
    /// </summary>
    public HttpClient ClientFrom(string ipAddress)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ipAddress);
        return client;
    }

    public IServiceScope NewScope() => Services.CreateScope();

    /// <summary>
    /// One lead, written straight to the database.
    ///
    /// The pipeline tests are about what happens to a lead after it exists, and going through the
    /// public form for each of them would mean every test also depended on the captcha, the rate
    /// limiter and the duplicate window - so a change to any of those would fail tests about
    /// merging. The capture path has its own tests.
    /// </summary>
    public async Task<Guid> AddLeadAsync(
        string nonce,
        DateTime? createdAtUtc = null,
        string? email = null,
        string? companyName = "Probe Industries")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sla = scope.ServiceProvider.GetRequiredService<SoftwareManagement.Application.Leads.ISlaCalculator>();

        var created = createdAtUtc ?? DateTime.UtcNow;

        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            FullName = $"PROBE-{nonce}",
            Email = email ?? $"probe-{nonce}@example.test",
            CompanyName = companyName,
            Message = $"An enquiry about the system, reference {nonce}.",
            Stage = LeadStage.New,
            Source = LeadSource.Direct,
            CreatedAtUtc = created,
            SlaDueAtUtc = sla.FirstResponseDueUtc(created),
            CreatedBy = "test-fixture",
        };

        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        // The audit stamp overwrites CreatedAtUtc on insert, which is right for the application and
        // wrong for a test that needs a lead from three days ago. Put it back, and put the deadline
        // back with it so the two agree.
        if (createdAtUtc is { } backdated)
        {
            await db.Leads.Where(l => l.Id == lead.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.CreatedAtUtc, backdated)
                    .SetProperty(l => l.SlaDueAtUtc, sla.FirstResponseDueUtc(backdated)));
        }

        return lead.Id;
    }

    private sealed record LoginRow(string AccessToken);
}

[CollectionDefinition(Name)]
public sealed class LeadTestGroup : ICollectionFixture<LeadFixture>
{
    public const string Name = "leads";
}
