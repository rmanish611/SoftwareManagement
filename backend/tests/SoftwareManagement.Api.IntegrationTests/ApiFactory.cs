using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace SoftwareManagement.Api.IntegrationTests;

/// <summary>
/// Hosts the real pipeline, including authentication and the security headers, so these tests
/// exercise what actually ships.
///
/// Configuration is supplied through environment variables rather than
/// <c>ConfigureAppConfiguration</c>: with top-level statements the application builds its own
/// configuration inside <c>WebApplication.CreateBuilder</c>, which runs before the factory's
/// configuration callbacks, so anything registered there arrives too late for startup
/// validation. Environment variables are read by the default configuration sources, so the
/// values are present at the moment the application needs them.
///
/// No developer secret is ever read by a test.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestIssuer = "software-management-tests";
    public const string TestAudience = "software-management-tests";

    /// <summary>A key used only in tests. It is not a secret and grants access to nothing real.</summary>
    public const string TestSigningKey = "integration-tests-signing-key-not-a-secret-0123456789";

    public const string TestConnectionString =
        "Server=.\\SQLEXPRESS;Database=SoftwareManagementDb_Tests;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly string[] ManagedVariables =
    [
        "ConnectionStrings__Default",
        "Jwt__Key",
        "Jwt__Issuer",
        "Jwt__Audience",
    ];

    private readonly Dictionary<string, string?> _saved = [];

    public ApiFactory()
    {
        foreach (var name in ManagedVariables)
        {
            _saved[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__Default", TestConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", TestSigningKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestAudience);
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
            foreach (var (name, value) in _saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        base.Dispose(disposing);
    }
}
