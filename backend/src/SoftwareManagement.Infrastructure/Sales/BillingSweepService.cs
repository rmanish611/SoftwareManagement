using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SoftwareManagement.Application.Sales;

namespace SoftwareManagement.Infrastructure.Sales;

/// <summary>
/// Runs the billing sweep on a timer.
///
/// Hourly rather than every few minutes: nothing it does is urgent to the minute. An invoice that
/// becomes overdue at midnight is chased within the hour, and a renewal raised fifteen days out is
/// raised on the right day whichever hour the pass happens to run.
///
/// The sweep takes its own database lock, so two of these running against one database do the work
/// once (NFR-DEP-05).
/// </summary>
public sealed partial class BillingSweepService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BillingSweepService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<BillingSweepService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("Billing:SweepEnabled", true))
        {
            LogSweepDisabled(_logger);
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(60, _configuration.GetValue("Billing:SweepIntervalSeconds", 3600)));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var billing = scope.ServiceProvider.GetRequiredService<IBillingService>();
                await billing.RunSweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or TimeoutException)
            {
                // One failed pass is not a reason to stop chasing money ever again.
                LogPassFailed(_logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The billing sweep is disabled by configuration. Invoices will not be escalated and renewals will not be raised.")]
    private static partial void LogSweepDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "A billing sweep pass failed. The next pass will pick up the same work.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
