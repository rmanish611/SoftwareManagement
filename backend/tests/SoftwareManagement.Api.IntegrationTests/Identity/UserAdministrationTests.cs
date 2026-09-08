using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Audit;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Identity;

/// <summary>
/// Creating, deactivating and re-roling back-office users, including the rule that keeps the system
/// from locking its own owner out (BR-IAM-04).
/// </summary>
[Trait("Category", "Integration")]
[Collection(IdentityTestGroup.Name)]
public sealed class UserAdministrationTests(IdentityFixture fixture)
{
    private readonly IdentityFixture _fixture = fixture;

    private static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@softwaremanagement.test";

    [Fact]
    public async Task REQ_IAM_006_CreatesAUserWhoCannotSignInUntilTheySetAPassword()
    {
        using var client = await _fixture.SignedInClientAsync(IdentityFixture.OwnerEmail);
        var email = UniqueEmail("invited");

        var created = await client.PostAsJsonAsync("/api/v1/admin/users",
            new { email, fullName = "Invited Person", role = "Sales" });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<UserRow>();
        body!.Email.Should().Be(email);
        body.IsActive.Should().BeFalse("an invited user is inactive until they set their own password");
        body.Roles.Should().Contain("Sales");

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        persisted.Should().NotBeNull();
        persisted!.PasswordHash.Should().BeNull("no administrator ever chooses another person's password");
    }

    [Fact]
    public async Task REQ_IAM_006_Returns409_WhenTheEmailAddressIsAlreadyRegistered()
    {
        using var client = await _fixture.SignedInClientAsync(IdentityFixture.OwnerEmail);

        var response = await client.PostAsJsonAsync("/api/v1/admin/users",
            new { email = IdentityFixture.SalesEmail, fullName = "Duplicate", role = "Editor" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("already has an account");
    }

    [Fact]
    public async Task REQ_IAM_006_Returns422_ForARoleThatDoesNotExist()
    {
        using var client = await _fixture.SignedInClientAsync(IdentityFixture.OwnerEmail);

        var response = await client.PostAsJsonAsync("/api/v1/admin/users",
            new { email = UniqueEmail("bad-role"), fullName = "Wrong Role", role = "Superuser" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task REQ_IAM_007_EndsAccessImmediatelyAndRevokesTheRefreshToken()
    {
        using var owner = await _fixture.SignedInClientAsync(IdentityFixture.OwnerEmail);
        var email = UniqueEmail("to-deactivate");

        var created = await owner.PostAsJsonAsync("/api/v1/admin/users",
            new { email, fullName = "Temporary Person", role = "Editor" });
        var user = await created.Content.ReadFromJsonAsync<UserRow>();

        // Give them a password and a live session, so deactivation has something real to end.
        using (var scope = _fixture.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Domain.Identity.AdminUser>>();
            var created2 = await users.FindByEmailAsync(email);
            created2!.IsActive = true;
            await users.AddPasswordAsync(created2, IdentityFixture.GoodPassword);
            await users.UpdateAsync(created2);
        }

        var token = await _fixture.SignInAsync(email);
        using var theirClient = _fixture.CreateClient();
        theirClient.DefaultRequestHeaders.Authorization = new("Bearer", token);
        (await theirClient.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode
            .Should().Be(HttpStatusCode.OK, "the session works before deactivation");

        var deactivated = await owner.PostAsync(new Uri($"/api/v1/admin/users/{user!.Id}/deactivate", UriKind.Relative), null);
        deactivated.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope2 = _fixture.Services.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var live = await db.RefreshTokens
            .Include(t => t.User)
            .Where(t => t.User!.Email == email && t.RevokedAtUtc == null)
            .CountAsync();

        live.Should().Be(0, "deactivation revokes every refresh token, so the session cannot be renewed");
    }

    [Fact]
    public async Task REQ_IAM_007_Returns409_WhenDeactivatingTheLastOwner()
    {
        using var client = await _fixture.SignedInClientAsync(IdentityFixture.OwnerEmail);

        var users = await client.GetFromJsonAsync<List<UserRow>>("/api/v1/admin/users");
        var owner = users!.Single(u => u.Email == IdentityFixture.OwnerEmail);

        var response = await client.PostAsync(new Uri($"/api/v1/admin/users/{owner.Id}/deactivate", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("last remaining Owner");
    }

    [Fact]
    public async Task REQ_IAM_007_Returns404_ForAUserThatDoesNotExist()
    {
        using var client = await _fixture.SignedInClientAsync(IdentityFixture.OwnerEmail);

        var response = await client.PostAsync(new Uri($"/api/v1/admin/users/{Guid.NewGuid()}/deactivate", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task REQ_IAM_004_ChangesAUsersRoleAndAuditsTheChange()
    {
        using var owner = await _fixture.SignedInClientAsync(IdentityFixture.OwnerEmail);
        var email = UniqueEmail("re-roled");

        var created = await owner.PostAsJsonAsync("/api/v1/admin/users",
            new { email, fullName = "Moves Around", role = "Editor" });
        var user = await created.Content.ReadFromJsonAsync<UserRow>();

        var assigned = await owner.PostAsJsonAsync($"/api/v1/admin/users/{user!.Id}/role", new { role = "Auditor" });
        assigned.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var audit = await db.AuditLogs
            .Where(a => a.EntityId == user.Id && a.Action == AuditActions.RoleAssigned)
            .OrderByDescending(a => a.OccurredAtUtc)
            .FirstOrDefaultAsync();

        audit.Should().NotBeNull();
        audit!.BeforeJson.Should().Contain("Editor");
        audit.AfterJson.Should().Contain("Auditor");
        audit.ActorEmail.Should().Be(IdentityFixture.OwnerEmail);
    }

    [Fact]
    public async Task REQ_IAM_004_Returns422_WhenAssigningARoleThatDoesNotExist()
    {
        using var owner = await _fixture.SignedInClientAsync(IdentityFixture.OwnerEmail);
        var users = await owner.GetFromJsonAsync<List<UserRow>>("/api/v1/admin/users");
        var target = users!.First(u => u.Email == IdentityFixture.EditorEmail);

        var response = await owner.PostAsJsonAsync($"/api/v1/admin/users/{target.Id}/role", new { role = "Wizard" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task REQ_IAM_006_ListsUsersForTheAuditorButRefusesTheEditor()
    {
        using var auditor = await _fixture.SignedInClientAsync(IdentityFixture.AuditorEmail);
        var readable = await auditor.GetAsync(new Uri("/api/v1/admin/users", UriKind.Relative));
        readable.StatusCode.Should().Be(HttpStatusCode.OK, "the Auditor reads everything and writes nothing");

        using var editor = await _fixture.SignedInClientAsync(IdentityFixture.EditorEmail);
        var refused = await editor.GetAsync(new Uri("/api/v1/admin/users", UriKind.Relative));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record UserRow(
        Guid Id,
        string Email,
        string FullName,
        bool IsActive,
        string[] Roles,
        DateTime? LastLoginAtUtc);
}
