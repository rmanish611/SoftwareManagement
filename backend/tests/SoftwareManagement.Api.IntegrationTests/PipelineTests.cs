using System.Net;
using AwesomeAssertions;

namespace SoftwareManagement.Api.IntegrationTests;

/// <summary>
/// The walking skeleton's contract: the process answers, refuses anonymous callers by default,
/// and returns errors in a machine-readable shape that leaks nothing.
/// </summary>
public sealed class PipelineTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Liveness_Answers200_WithoutTouchingTheDatabase()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PublicPing_IsReachableAnonymously()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/ping/public", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("public");
    }

    [Fact]
    public async Task SecurePing_Returns401_ForAnAnonymousCaller()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/ping/secure", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "deny-by-default means an endpoint without an explicit opt-out is unreachable without a token");
    }

    [Fact]
    public async Task SecurePing_Returns401_ForAGarbageToken()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-real-token");

        var response = await client.GetAsync(new Uri("/api/v1/ping/secure", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EveryResponse_CarriesTheRequiredSecurityHeaders()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/ping/public", UriKind.Relative));

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Contain("strict-origin-when-cross-origin");
        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle()
            .Which.Should().Contain("frame-ancestors 'none'").And.NotContain("script-src 'self' 'unsafe-inline'");
    }

    [Fact]
    public async Task UnknownRoute_Returns404_AndNeverLeaksAStackTrace()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/there-is-no-such-route", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body.Should().NotContain(".cs:line").And.NotContain("   at SoftwareManagement");
    }
}
