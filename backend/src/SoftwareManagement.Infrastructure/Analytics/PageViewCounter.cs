using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoftwareManagement.Application.Analytics;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Domain.Analytics;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Analytics;

/// <summary>
/// Increments the day's counter for a public path.
///
/// Two things make this less trivial than it looks. Two people opening the same page at the same
/// moment must not produce two rows for the same day, so the increment is a single atomic UPDATE
/// and a row is inserted only when that UPDATE matched nothing. And a counter must never be the
/// reason a visitor sees an error, so a failure here is logged and swallowed rather than raised:
/// the page is the product, the statistic is not.
/// </summary>
public sealed partial class PageViewCounter(
    AppDbContext dbContext,
    IEditorTimeZone timeZone,
    ILogger<PageViewCounter> logger) : IPageViewCounter
{
    /// <summary>Longer than any real path the site serves, and short enough to fit the column.</summary>
    public const int MaxPathLength = 400;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly IEditorTimeZone _timeZone = timeZone;
    private readonly ILogger<PageViewCounter> _logger = logger;

    public Task RecordViewAsync(string path, bool isNewArrival, CancellationToken cancellationToken) =>
        BumpAsync(path, views: 1, uniqueVisitors: isNewArrival ? 1 : 0, formStarts: 0, formSubmits: 0, cancellationToken);

    public Task RecordFormStartAsync(string path, CancellationToken cancellationToken) =>
        BumpAsync(path, views: 0, uniqueVisitors: 0, formStarts: 1, formSubmits: 0, cancellationToken);

    public Task RecordFormSubmitAsync(string path, CancellationToken cancellationToken) =>
        BumpAsync(path, views: 0, uniqueVisitors: 0, formStarts: 0, formSubmits: 1, cancellationToken);

    private async Task BumpAsync(
        string path,
        int views,
        int uniqueVisitors,
        int formStarts,
        int formSubmits,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalised = Normalise(path);

        // The day is the company's day, not UTC. A view at 02:00 in Kolkata belongs to that date on
        // the report the owner reads, not to the one before it (A-08).
        var statDate = DateOnly.FromDateTime(_timeZone.ToEditorLocal(DateTime.UtcNow));

        try
        {
            var updated = await _dbContext.PageViewStats
                .Where(s => s.Path == normalised && s.StatDate == statDate)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(x => x.Views, x => x.Views + views)
                        .SetProperty(x => x.UniqueVisitors, x => x.UniqueVisitors + uniqueVisitors)
                        .SetProperty(x => x.FormStarts, x => x.FormStarts + formStarts)
                        .SetProperty(x => x.FormSubmits, x => x.FormSubmits + formSubmits),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                return;
            }

            _dbContext.PageViewStats.Add(new PageViewStat
            {
                Id = Guid.NewGuid(),
                Path = normalised,
                StatDate = statDate,
                Views = views,
                UniqueVisitors = uniqueVisitors,
                FormStarts = formStarts,
                FormSubmits = formSubmits,
                CreatedBy = "site",
            });

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            // Another request inserted the row for this path and day between the UPDATE and the
            // INSERT. The unique index caught it, which is what it is for; the retry below adds the
            // counts to the row that won.
            _dbContext.ChangeTracker.Clear();

            try
            {
                await _dbContext.PageViewStats
                    .Where(s => s.Path == normalised && s.StatDate == statDate)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(x => x.Views, x => x.Views + views)
                            .SetProperty(x => x.UniqueVisitors, x => x.UniqueVisitors + uniqueVisitors)
                            .SetProperty(x => x.FormStarts, x => x.FormStarts + formStarts)
                            .SetProperty(x => x.FormSubmits, x => x.FormSubmits + formSubmits),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DbUpdateException retryFailure)
            {
                LogCounterFailed(_logger, normalised, retryFailure);
            }

            LogCounterRaced(_logger, normalised, exception);
        }
    }

    /// <summary>
    /// Query strings and fragments are dropped, so one page is one row however it was linked to,
    /// and an over-long path is truncated rather than rejected: a statistic is not worth failing a
    /// request over.
    /// </summary>
    private static string Normalise(string path)
    {
        var cut = path.IndexOfAny(['?', '#']);
        var trimmed = cut >= 0 ? path[..cut] : path;

        if (trimmed.Length > 1)
        {
            trimmed = trimmed.TrimEnd('/');
        }

        if (trimmed.Length == 0)
        {
            trimmed = "/";
        }

        return trimmed.Length > MaxPathLength ? trimmed[..MaxPathLength] : trimmed;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Two requests created the view counter for {path} at once; the counts were merged.")]
    private static partial void LogCounterRaced(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The view counter for {path} could not be written. The page was served regardless.")]
    private static partial void LogCounterFailed(ILogger logger, string path, Exception exception);
}
