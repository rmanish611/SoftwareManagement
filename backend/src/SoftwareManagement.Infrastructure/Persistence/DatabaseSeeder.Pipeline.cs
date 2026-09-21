using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Domain.Content;

namespace SoftwareManagement.Infrastructure.Persistence;

/// <summary>
/// The days the office is shut.
///
/// Only the fixed national holidays are seeded. The ones that move - Diwali, Holi, Eid - change
/// date every year, and a recurring row for them would close the office on the wrong day and open
/// it on the right one. The owner adds those from the settings screen, which is why they are rows
/// rather than a constant in the SLA calculator (BR-LEAD-10).
/// </summary>
public sealed partial class DatabaseSeeder
{
    private static readonly (int Month, int Day, string Name)[] FixedHolidays =
    [
        (1, 26, "Republic Day"),
        (5, 1, "Labour Day"),
        (8, 15, "Independence Day"),
        (10, 2, "Gandhi Jayanti"),
        (12, 25, "Christmas Day"),
    ];

    private async Task SeedHolidaysAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Holidays
            .AsNoTracking()
            .Select(h => h.HolidayDate)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var taken = new HashSet<DateOnly>(existing);
        var holidays = new List<Holiday>();

        foreach (var (month, day, name) in FixedHolidays)
        {
            // The year is arbitrary for a recurring row - only the month and day are read - but a
            // date column has to hold one, and a real one reads better in the table than 0001.
            var date = new DateOnly(2026, month, day);

            if (taken.Contains(date))
            {
                continue;
            }

            holidays.Add(new Holiday
            {
                Id = Guid.NewGuid(),
                HolidayDate = date,
                Name = name,
                IsRecurring = true,
                CreatedBy = "seed",
            });
        }

        if (holidays.Count > 0)
        {
            _dbContext.Holidays.AddRange(holidays);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
