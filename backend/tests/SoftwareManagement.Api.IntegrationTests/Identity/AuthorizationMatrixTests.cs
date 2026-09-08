using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Identity;

namespace SoftwareManagement.Api.IntegrationTests.Identity;

/// <summary>
/// The authorization matrix from `docs/blueprint/06-authz.md`, asserted against the real endpoints.
/// Hiding a button is not authorization: every case here calls the API directly with the wrong role
/// and requires the server to refuse.
/// </summary>
[Trait("Category", "Integration")]
[Collection(IdentityTestGroup.Name)]
public sealed class AuthorizationMatrixTests(IdentityFixture fixture)
{
    private readonly IdentityFixture _fixture = fixture;

    [Fact]
    public async Task REQ_IAM_004_GivesEachRoleExactlyThePermissionsTheMatrixGrantsIt()
    {
        foreach (var (email, role) in new[]
        {
            (IdentityFixture.OwnerEmail, RoleNames.Owner),
            (IdentityFixture.SalesEmail, RoleNames.Sales),
            (IdentityFixture.EditorEmail, RoleNames.Editor),
            (IdentityFixture.AuditorEmail, RoleNames.Auditor),
        })
        {
            using var client = _fixture.CreateClient();
            var login = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email, password = IdentityFixture.GoodPassword, twoFactorCode = (string?)null });

            var body = await login.Content.ReadFromJsonAsync<IdentityFixture.LoginResponseBody>();

            body!.Roles.Should().BeEquivalentTo([role]);
            body.Permissions.Should().BeEquivalentTo(RolePermissionMap.For(role),
                "the token for {0} must carry exactly the permissions the matrix grants", role);
        }
    }

    [Fact]
    public async Task REQ_IAM_004_Returns403_WhenAnEditorCallsAnAdministrationEndpoint()
    {
        using var client = await _fixture.SignedInClientAsync(IdentityFixture.EditorEmail);

        var response = await client.GetAsync(new Uri("/api/v1/admin/users", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an Editor has no administration permission, and the API must say so rather than the UI hiding a link");
    }

    [Fact]
    public async Task REQ_IAM_005_Returns401_ForEveryAdministrationRouteWithoutAToken()
    {
        using var client = _fixture.CreateClient();

        foreach (var path in new[]
        {
            "/api/v1/admin/users",
            "/api/v1/admin/login-attempts",
            "/api/v1/auth/me",
        })
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "{0} is not on the anonymous allowlist", path);
        }
    }

    [Fact]
    public async Task REQ_IAM_005_AllowsExactlyTheFrozenAnonymousAllowlist()
    {
        using var client = _fixture.CreateClient();

        foreach (var path in new[] { "/health", "/health/live", "/health/ready" })
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative));
            response.StatusCode.Should().Be(HttpStatusCode.OK, "{0} is on the allowlist", path);
        }

        // Login and refresh are on the allowlist too: they answer, they do not demand a token first.
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "someone@softwaremanagement.test", password = "whatever", twoFactorCode = (string?)null });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the endpoint is reachable and rejects the credentials");

        var refresh = await client.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null);
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "reachable, and rejects a missing refresh token");
    }

    [Fact]
    public async Task REQ_IAM_005_GivesTheAuditorNoWritePermissionAtAll()
    {
        var auditorPermissions = RolePermissionMap.For(RoleNames.Auditor);

        auditorPermissions.Should().NotBeEmpty();
        auditorPermissions.Where(RolePermissionMap.IsWritePermission)
            .Should().BeEmpty("a read-only role that can write is not read-only");

        using var client = await _fixture.SignedInClientAsync(IdentityFixture.AuditorEmail);

        var write = await client.PostAsJsonAsync("/api/v1/admin/users",
            new { email = "auditor-should-not-create@softwaremanagement.test", fullName = "Nope", role = "Editor" });

        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task REQ_IAM_005_GivesTheEditorNoAccessToPersonalDataOrMoney()
    {
        var editorPermissions = RolePermissionMap.For(RoleNames.Editor);

        editorPermissions.Should().NotContain(Permissions.Lead.Read);
        editorPermissions.Should().NotContain(Permissions.Crm.OrganisationRead);
        editorPermissions.Should().NotContain(Permissions.Finance.InvoiceRead);
        editorPermissions.Should().NotContain(Permissions.Administration.SettingRead);
    }

    [Fact]
    public async Task REQ_IAM_011_LetsTheAuditorReadSignInAttempts()
    {
        // Produce one failure so the log has something in it.
        using var anonymous = _fixture.CreateClient();
        await anonymous.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "audit-view-target@softwaremanagement.test", password = "wrong", twoFactorCode = (string?)null });

        using var client = await _fixture.SignedInClientAsync(IdentityFixture.AuditorEmail);

        var response = await client.GetAsync(new Uri("/api/v1/admin/login-attempts?failuresOnly=true&take=10", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var attempts = await response.Content.ReadFromJsonAsync<List<LoginAttemptRow>>();
        attempts.Should().NotBeNull();
        attempts!.Should().OnlyContain(a => !a.Succeeded);
        attempts.Should().Contain(a => a.EmailAttempted == "audit-view-target@softwaremanagement.test");
    }

    [Fact]
    public async Task REQ_IAM_011_Returns403_WhenAnEditorAsksForTheSignInLog()
    {
        using var client = await _fixture.SignedInClientAsync(IdentityFixture.EditorEmail);

        var response = await client.GetAsync(new Uri("/api/v1/admin/login-attempts", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task REQ_IAM_011_ClampsThePageSize_SoOneRequestCannotDrainTheTable()
    {
        using var client = await _fixture.SignedInClientAsync(IdentityFixture.OwnerEmail);

        var response = await client.GetAsync(new Uri("/api/v1/admin/login-attempts?take=100000", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var attempts = await response.Content.ReadFromJsonAsync<List<LoginAttemptRow>>();
        attempts!.Count.Should().BeLessThanOrEqualTo(100, "NFR-PERF-04 caps every list endpoint at 100 rows");
    }

    private sealed record LoginAttemptRow(
        DateTime OccurredAtUtc,
        string EmailAttempted,
        bool Succeeded,
        string IpAddress,
        string? FailureReason);
}
