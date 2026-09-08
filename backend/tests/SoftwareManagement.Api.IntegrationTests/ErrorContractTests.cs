using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace SoftwareManagement.Api.IntegrationTests;

/// <summary>
/// NFR-OBS-03: every error is an RFC 9457 problem document with a traceId, and no error ever
/// carries a stack trace or an inner exception message out to a caller.
/// </summary>
public sealed class ErrorContractTests
{
    private sealed class DiagnosticsEnabledFactory : WebApplicationFactory<Program>
    {
        public DiagnosticsEnabledFactory()
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", ApiFactory.TestConnectionString);
            Environment.SetEnvironmentVariable("Jwt__Key", ApiFactory.TestSigningKey);
            Environment.SetEnvironmentVariable("Jwt__Issuer", ApiFactory.TestIssuer);
            Environment.SetEnvironmentVariable("Jwt__Audience", ApiFactory.TestAudience);
            Environment.SetEnvironmentVariable("Diagnostics__EnableThrowEndpoint", "true");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.UseEnvironment(Environments.Production);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Environment.SetEnvironmentVariable("Diagnostics__EnableThrowEndpoint", null);
            }

            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task UnhandledException_ReturnsRfc9457ProblemDetails_WithATraceIdAndNoStackTrace()
    {
        using var factory = new DiagnosticsEnabledFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(new Uri("/api/v1/diagnostics/throw", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        root.TryGetProperty("title", out _).Should().BeTrue();
        root.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetInt32().Should().Be(500);
        root.TryGetProperty("traceId", out var traceId).Should().BeTrue("a caller must be able to quote one id when reporting a fault");
        traceId.GetString().Should().NotBeNullOrWhiteSpace();

        body.Should().NotContain(".cs:line");
        body.Should().NotContain("Deliberate failure raised by the diagnostics endpoint",
            "an exception message can carry internal detail and must never reach a caller");
    }

    [Fact]
    public async Task DiagnosticsEndpoint_Returns404_WhenItIsNotExplicitlyEnabled()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/diagnostics/throw", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a disabled diagnostics endpoint must not announce that it exists");
    }
}
