namespace SoftwareManagement.Api.IntegrationTests;

/// <summary>
/// The walking-skeleton factory: the real pipeline with nothing switched on beyond what every
/// request needs. Tests that assert a feature is off by default depend on this fixture declaring
/// nothing extra, which <see cref="ConfiguredApiFactory"/> now guarantees.
///
/// No developer secret is ever read by a test.
/// </summary>
public sealed class ApiFactory : ConfiguredApiFactory
{
    public const string TestIssuer = "software-management-tests";
    public const string TestAudience = "software-management-tests";

    /// <summary>A key used only in tests. It is not a secret and grants access to nothing real.</summary>
    public const string TestSigningKey = "integration-tests-signing-key-not-a-secret-0123456789";

    public const string TestConnectionString =
        "Server=.\\SQLEXPRESS;Database=SoftwareManagementDb_Tests;Trusted_Connection=True;TrustServerCertificate=True";

    protected override IReadOnlyDictionary<string, string?> Settings { get; } =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings__Default"] = TestConnectionString,
            ["Jwt__Key"] = TestSigningKey,
            ["Jwt__Issuer"] = TestIssuer,
            ["Jwt__Audience"] = TestAudience,

            // A schema is not a feature. Without this the fixture ran against whatever this
            // machine's test database happened to contain, so every new migration left it a phase
            // behind until somebody updated it by hand - and the symptom was "Invalid object name"
            // raised by the seeder inside tests that have nothing to do with the new table.
            ["Database__MigrateOnStartup"] = "true",
        };
}
