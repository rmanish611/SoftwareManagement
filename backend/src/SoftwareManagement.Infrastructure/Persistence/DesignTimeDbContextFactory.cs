using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Infrastructure.Time;

namespace SoftwareManagement.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet dotnet-ef migrations add` work without starting the API. The connection string
/// comes from the environment so no credential is ever compiled in; the value is only used to
/// pick a provider at design time, never to reach a production database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    // ADR-R01: development runs against the already-running SQL Express instance, because
    // LocalDB stopped answering on its own named pipe on this machine.
    private const string DefaultDesignTimeConnection =
        "Server=.\\SQLEXPRESS;Database=SoftwareManagementDb;Trusted_Connection=True;TrustServerCertificate=True";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? DefaultDesignTimeConnection;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options, new SystemClock());
    }
}
