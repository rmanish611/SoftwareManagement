using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Infrastructure;

/// <summary>
/// Readiness means the database actually answers, not that the process started
/// (NFR-AVAIL-04). Liveness deliberately does not touch the database, so a database outage
/// does not cause an orchestrator to restart a healthy process.
/// </summary>
public sealed class DatabaseHealthCheck(AppDbContext dbContext) : IHealthCheck
{
    private readonly AppDbContext _dbContext = dbContext;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);

            return canConnect
                ? HealthCheckResult.Healthy("database reachable")
                : HealthCheckResult.Unhealthy("database did not answer");
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or DbUpdateException)
        {
            return HealthCheckResult.Unhealthy("database check failed", ex);
        }
    }
}
