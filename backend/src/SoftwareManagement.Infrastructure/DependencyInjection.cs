using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Infrastructure.Persistence;
using SoftwareManagement.Infrastructure.Time;

namespace SoftwareManagement.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the application needs to reach the outside world. The connection
    /// string is required: a missing one fails at startup with a named error rather than at the
    /// first request with a null reference (NFR-DEP-01).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connection = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured. Set it with `dotnet user-secrets` in development " +
                "or the ConnectionStrings__Default environment variable in production. See docs/ENVIRONMENT.md.");
        }

        services.AddSingleton<IClock, SystemClock>();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connection, sql =>
            {
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
            }));

        return services;
    }
}
