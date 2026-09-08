using System.Net;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests;

/// <summary>
/// Readiness genuinely opens a database connection, so these tests need SQL Server and carry
/// the Integration trait. GitHub's hosted runners have no SQL Server, so CI filters them out
/// and prints the filtered count; the local phase gate runs them for real (NFR-MAINT-04).
///
/// The fixture applies the migrations to a dedicated test database, which also proves that a
/// database can be built purely from migrations - the property the clean-clone acceptance
/// test depends on (NFR-DEP-02).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ReadinessTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Readiness_Answers200_WhenTheDatabaseIsReachable()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the readiness probe reports 503 when SQL Server does not answer, so 200 proves it does");
    }

    [Fact]
    public async Task AggregateHealth_Answers200_WhenEveryCheckPasses()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MigrationsHistory_ContainsTheInitialMigration_SoTheSchemaIsBuiltFromMigrationsAlone()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var applied = await db.Database.GetAppliedMigrationsAsync();

        applied.Should().Contain(id => id.EndsWith("InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Model_HasNoPendingChanges_SoTheDatabaseMatchesTheCode()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();

        pending.Should().BeEmpty("a pending migration means the deployed schema would not match the model");
    }
}
