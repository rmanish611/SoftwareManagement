namespace SoftwareManagement.Api.IntegrationTests.Identity;

/// <summary>
/// One shared identity fixture for every identity test class.
///
/// With a per-class fixture each class builds its own host and seeds the same database at the same
/// time, which is a genuine race: two callers both find no Sales user and both insert one. A
/// collection fixture is created once, so the seeding happens once and the tests still run against
/// the real pipeline. The application's own seeding race is fixed separately and properly, with a
/// SQL Server application lock, because two production instances can start together too.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IdentityTestGroup : ICollectionFixture<IdentityFixture>
{
    public const string Name = "identity";
}
