using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SoftwareManagement.Application.Leads;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Leads;

/// <summary>
/// The holiday list, read once and held.
///
/// The SLA clock asks this question up to a fortnight of times for a single lead, and the answer
/// changes perhaps a dozen times a year. Going to the database for each day would turn one lead's
/// deadline into fourteen round trips (NFR-PERF-05).
///
/// The cache is short rather than indefinite so that adding a holiday takes effect the same
/// morning without a restart, which is when somebody will be adding one.
/// </summary>
public sealed class HolidayCalendar(AppDbContext dbContext, IMemoryCache cache) : IHolidayCalendar
{
    private const string CacheKey = "holidays:all";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _dbContext = dbContext;
    private readonly IMemoryCache _cache = cache;

    public bool IsHoliday(DateOnly localDate) => Holidays().Any(h => h.Falls(localDate));

    private IReadOnlyList<Holiday> Holidays() =>
        _cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;

            // Synchronous on purpose: ISlaCalculator is synchronous because it is called from
            // inside loops over days, and making it asynchronous would spread await through
            // arithmetic that never touches the network.
            return (IReadOnlyList<Holiday>)[.. _dbContext.Holidays.AsNoTracking().ToList()];
        }) ?? [];
}
