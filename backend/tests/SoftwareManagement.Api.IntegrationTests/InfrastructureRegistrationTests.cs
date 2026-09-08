using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Infrastructure;
using SoftwareManagement.Infrastructure.Persistence;
using SoftwareManagement.Infrastructure.Time;

namespace SoftwareManagement.Api.IntegrationTests;

/// <summary>
/// Composition-root behaviour. These tests need no database: they prove the application fails
/// loudly and early when it is misconfigured, which is what NFR-DEP-01 promises a new machine.
/// </summary>
public sealed class InfrastructureRegistrationTests
{
    private static IConfiguration ConfigurationWith(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void AddInfrastructure_Throws_WhenTheConnectionStringIsMissing()
    {
        var services = new ServiceCollection();
        var configuration = ConfigurationWith(("Jwt:Key", "irrelevant"));

        var act = () => services.AddInfrastructure(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:Default*")
            .WithMessage("*docs/ENVIRONMENT.md*",
                "a misconfigured machine must be told exactly which value to set and where it is documented");
    }

    [Fact]
    public void AddInfrastructure_Throws_WhenTheConnectionStringIsBlank()
    {
        var services = new ServiceCollection();
        var configuration = ConfigurationWith(("ConnectionStrings:Default", "   "));

        var act = () => services.AddInfrastructure(configuration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddInfrastructure_RegistersTheDbContextAndTheClock()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(ConfigurationWith(("ConnectionStrings:Default", ApiFactory.TestConnectionString)));

        using var provider = services.BuildServiceProvider();

        provider.GetService<IClock>().Should().BeOfType<SystemClock>();
        provider.GetService<AppDbContext>().Should().NotBeNull();
    }

    [Fact]
    public void DesignTimeFactory_CreatesAContextSoMigrationsCanBeGeneratedWithoutStartingTheApi()
    {
        var factory = new DesignTimeDbContextFactory();

        using var context = factory.CreateDbContext([]);

        context.Should().NotBeNull();
        context.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.SqlServer");
    }

    [Fact]
    public void SystemClock_ReturnsUtc_NeverLocalTime()
    {
        var clock = new SystemClock();

        var now = clock.UtcNow;

        now.Kind.Should().Be(DateTimeKind.Utc, "every stored instant is UTC (A-08)");
        now.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
