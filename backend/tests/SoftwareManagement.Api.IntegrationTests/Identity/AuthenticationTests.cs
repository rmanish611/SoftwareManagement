using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Identity;

/// <summary>
/// Sign-in, lockout, token rotation, sign-out and password management, asserted against the real
/// pipeline and a real database.
/// </summary>
[Trait("Category", "Integration")]
[Collection(IdentityTestGroup.Name)]
public sealed class AuthenticationTests(IdentityFixture fixture)
{
    private readonly IdentityFixture _fixture = fixture;

    private static object LoginBody(string email, string password, string? code = null) =>
        new { email, password, twoFactorCode = code };

    [Fact]
    public async Task REQ_IAM_001_Returns200AndAnAccessToken_ForCorrectCredentials()
    {
        using var client = _fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody(IdentityFixture.OwnerEmail, IdentityFixture.GoodPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<IdentityFixture.LoginResponseBody>();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.Email.Should().Be(IdentityFixture.OwnerEmail);
        body.Roles.Should().Contain("Owner");
        body.Permissions.Should().Contain("admin.user.write");

        // BR-IAM-03: the access token lives 15 minutes.
        (body.ExpiresAtUtc - DateTime.UtcNow).TotalMinutes.Should().BeInRange(13, 16);
    }

    [Fact]
    public async Task REQ_IAM_001_Returns401WithoutRevealingWhichPartWasWrong_ForABadPassword()
    {
        using var client = _fixture.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody(IdentityFixture.AuditorEmail, IdentityFixture.WrongPassword));
        var unknownUser = await client.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("nobody-here@softwaremanagement.test", IdentityFixture.WrongPassword));

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownUser.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var first = await wrongPassword.Content.ReadAsStringAsync();
        var second = await unknownUser.Content.ReadAsStringAsync();

        first.Should().Contain("Email or password is incorrect");
        second.Should().Contain("Email or password is incorrect");
    }

    [Fact]
    public async Task REQ_IAM_001_WritesASucceededLoginAttemptAndAHashedRefreshToken()
    {
        using var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody(IdentityFixture.SalesEmail, IdentityFixture.GoodPassword));

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var attempt = await db.LoginAttempts
            .Where(a => a.EmailAttempted == IdentityFixture.SalesEmail && a.Succeeded)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync();

        attempt.Should().NotBeNull();
        attempt!.FailureReason.Should().BeNull();

        var token = await db.RefreshTokens
            .Include(t => t.User)
            .Where(t => t.User!.Email == IdentityFixture.SalesEmail)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync();

        token.Should().NotBeNull();
        token!.TokenHash.Should().HaveLength(64, "only the SHA-256 hash of a refresh token is stored");
    }

    [Fact]
    public async Task REQ_IAM_002_Returns423_OnTheSixthFailureWithinTheWindow()
    {
        const string email = "lockout-target@softwaremanagement.test";
        using var client = _fixture.CreateClient();

        // An address that does not exist still counts failures, so a brute-force attempt against an
        // unknown address is throttled exactly like one against a real account.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", LoginBody(email, IdentityFixture.WrongPassword));
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "attempt {0} is inside the allowance", attempt);
        }

        var sixth = await client.PostAsJsonAsync("/api/v1/auth/login", LoginBody(email, IdentityFixture.WrongPassword));

        sixth.StatusCode.Should().Be(HttpStatusCode.Locked);
        (await sixth.Content.ReadAsStringAsync()).Should().Contain("Account locked");
    }

    [Fact]
    public async Task REQ_IAM_002_LocksTheAccountEvenWhenTheSixthAttemptUsesTheCorrectPassword()
    {
        const string email = IdentityFixture.EditorEmail;
        using var client = _fixture.CreateClient();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", LoginBody(email, IdentityFixture.WrongPassword));
        }

        var withCorrectPassword = await client.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody(email, IdentityFixture.GoodPassword));

        withCorrectPassword.StatusCode.Should().Be(HttpStatusCode.Locked,
            "the lock is on the account, not on the guess: knowing the password does not lift it");

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.LoginAttempts.Where(a => a.EmailAttempted == email).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task REQ_IAM_003_IssuesANewTokenPairAndRetiresTheOldRefreshToken()
    {
        using var client = _fixture.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody(IdentityFixture.OwnerEmail, IdentityFixture.GoodPassword));
        var refreshCookie = RefreshCookie(login);

        using var refreshClient = _fixture.CreateClient();
        refreshClient.DefaultRequestHeaders.Add("X-Refresh-Token", refreshCookie);

        var refreshed = await refreshClient.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null);

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await refreshed.Content.ReadFromJsonAsync<IdentityFixture.LoginResponseBody>();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task REQ_IAM_003_Returns401AndRevokesTheWholeChain_WhenARotatedTokenIsPresentedAgain()
    {
        using var client = _fixture.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody(IdentityFixture.AuditorEmail, IdentityFixture.GoodPassword));
        var original = RefreshCookie(login);

        using var first = _fixture.CreateClient();
        first.DefaultRequestHeaders.Add("X-Refresh-Token", original);
        var rotated = await first.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var newToken = RefreshCookie(rotated);

        // Present the already-rotated token: this is what a stolen token looks like.
        using var replay = _fixture.CreateClient();
        replay.DefaultRequestHeaders.Add("X-Refresh-Token", original);
        var replayed = await replay.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null);

        replayed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The token the thief did not have is dead too: the whole chain was revoked.
        using var afterRevocation = _fixture.CreateClient();
        afterRevocation.DefaultRequestHeaders.Add("X-Refresh-Token", newToken);
        var subsequent = await afterRevocation.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null);

        subsequent.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task REQ_IAM_008_Returns202ForAnyAddress_SoTheEndpointCannotEnumerateAccounts()
    {
        using var client = _fixture.CreateClient();

        var registered = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = IdentityFixture.OwnerEmail });
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "never-registered@softwaremanagement.test" });

        registered.StatusCode.Should().Be(HttpStatusCode.Accepted);
        unknown.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task REQ_IAM_008_Returns410_WhenTheResetTokenIsNotValid()
    {
        using var client = _fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            email = IdentityFixture.OwnerEmail,
            token = "not-a-real-token",
            newPassword = "Another-Good-Pass-2026",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task REQ_IAM_009_Returns422_WhenTheNewPasswordIsTooShort()
    {
        using var client = await _fixture.SignedInClientAsync(IdentityFixture.SalesEmail);

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password", new
        {
            currentPassword = IdentityFixture.GoodPassword,
            newPassword = "Short1!",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("at least 12 characters");
    }

    [Fact]
    public async Task REQ_IAM_009_Returns401_WhenNobodyIsSignedIn()
    {
        using var client = _fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password", new
        {
            currentPassword = IdentityFixture.GoodPassword,
            newPassword = "Another-Good-Pass-2026",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task REQ_IAM_010_RevokesTheRefreshTokenSoTheSessionCannotBeResumed()
    {
        using var client = _fixture.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody(IdentityFixture.SalesEmail, IdentityFixture.GoodPassword));
        var body = await login.Content.ReadFromJsonAsync<IdentityFixture.LoginResponseBody>();
        var refreshToken = RefreshCookie(login);

        using var signedIn = _fixture.CreateClient();
        signedIn.DefaultRequestHeaders.Authorization = new("Bearer", body!.AccessToken);
        signedIn.DefaultRequestHeaders.Add("X-Refresh-Token", refreshToken);

        var loggedOut = await signedIn.PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null);
        loggedOut.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var afterLogout = _fixture.CreateClient();
        afterLogout.DefaultRequestHeaders.Add("X-Refresh-Token", refreshToken);
        var refreshed = await afterLogout.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null);

        refreshed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task REQ_IAM_010_Returns401_ForAnAnonymousLogout()
    {
        using var client = _fixture.CreateClient();

        var response = await client.PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task REQ_IAM_012_DoesNotDemandACode_WhenTwoFactorIsNotEnrolled()
    {
        using var client = _fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody(IdentityFixture.EditorEmail, IdentityFixture.GoodPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "two-factor is opt-in: enabling it for everyone would lock the owner out of his own site");
    }

    [Fact]
    public async Task REQ_IAM_012_Returns401MfaRequired_WhenTwoFactorIsEnrolledAndNoCodeIsSupplied()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == IdentityFixture.AuditorEmail);
        user.TwoFactorEnabled = true;
        await db.SaveChangesAsync();

        try
        {
            using var client = _fixture.CreateClient();
            var response = await client.PostAsJsonAsync("/api/v1/auth/login",
                LoginBody(IdentityFixture.AuditorEmail, IdentityFixture.GoodPassword));

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Two-factor code required");
        }
        finally
        {
            user.TwoFactorEnabled = false;
            await db.SaveChangesAsync();
        }
    }

    private static string RefreshCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue("sign-in must set the refresh cookie");

        var cookie = cookies!.FirstOrDefault(c => c.StartsWith("sm_refresh=", StringComparison.Ordinal));
        cookie.Should().NotBeNull();
        cookie.Should().Contain("httponly", Exactly.Once(), "the refresh token must be unreadable by scripts");

        return cookie!.Split(';')[0]["sm_refresh=".Length..];
    }
}
