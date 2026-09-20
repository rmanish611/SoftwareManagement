using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace SoftwareManagement.Api.IntegrationTests;

/// <summary>
/// Hosts the real pipeline, including authentication and the security headers, against whatever
/// configuration the deriving fixture declares in <see cref="Settings"/>.
///
/// Configuration has to travel through environment variables. With top-level statements the
/// application builds its own configuration inside <c>WebApplication.CreateBuilder</c>, which runs
/// before <c>ConfigureAppConfiguration</c> callbacks, so anything registered there arrives after
/// start-up has already read the connection string and the signing key.
///
/// Environment variables are process-wide, and that is the hazard this type exists to remove.
/// A factory does not build its host in its constructor; it builds it on the first call that needs
/// a server. So a fixture that set its variables in a constructor could have them overwritten by a
/// second fixture constructed in the meantime, and would then quietly run against the other
/// fixture's database. The failure that produces lands on whichever assertion happens to depend on
/// a row count, and moves between runs, which reads as flakiness rather than as the configuration
/// fault it is.
///
/// <see cref="CreateHost"/> is the one moment when the entry point actually runs and reads
/// configuration, so the variables are applied there, under a process-wide lock, and cleared again
/// before the lock is released. Every managed variable is cleared first, so a fixture inherits
/// nothing from the one before it: what a host reads is exactly what its own fixture declared.
/// </summary>
public abstract class ConfiguredApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Every variable any fixture in this assembly sets. Clearing the whole set before applying one
    /// fixture's values is what stops a setting leaking forward - a captcha bypass or a disabled
    /// outbox pump left behind by an earlier fixture would otherwise silently weaken a later test.
    /// </summary>
    private static readonly string[] ManagedVariables =
    [
        "ConnectionStrings__Default",
        "Jwt__Key",
        "Jwt__Issuer",
        "Jwt__Audience",
        "Database__MigrateOnStartup",
        "Database__SeedOnStartup",
        "Seed__OwnerEmail",
        "Seed__OwnerPassword",
        "Captcha__BypassToken",
        "Outbox__PumpEnabled",
        "Media__RootPath",
        "Diagnostics__EnableThrowEndpoint",
    ];

    private static readonly object HostGate = new();

    /// <summary>The configuration this fixture's host is to be built with, and nothing else.</summary>
    protected abstract IReadOnlyDictionary<string, string?> Settings { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(Environments.Production);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        lock (HostGate)
        {
            Apply(Settings);

            try
            {
                // The entry point runs inside this call, so this is the only instant at which the
                // environment has to be correct - and the lock guarantees it is ours for all of it.
                return base.CreateHost(builder);
            }
            finally
            {
                Clear();
            }
        }
    }

    private static void Apply(IReadOnlyDictionary<string, string?> settings)
    {
        Clear();

        foreach (var (name, value) in settings)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static void Clear()
    {
        foreach (var name in ManagedVariables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
