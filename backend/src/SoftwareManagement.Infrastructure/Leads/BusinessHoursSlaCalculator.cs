using System.Globalization;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Application.Leads;

namespace SoftwareManagement.Infrastructure.Leads;

/// <summary>
/// When a first reply is owed.
///
/// Counted in the company's working hours rather than in wall-clock hours. An enquiry that arrives
/// at 23:00 on a Saturday is not nine hours late by Sunday morning; it is due nine working hours
/// after the office next opens. A deadline that is already breached before anyone could have read
/// the message teaches people to ignore the deadline.
/// </summary>
public sealed class BusinessHoursSlaCalculator(
    ISystemSettings settings,
    IEditorTimeZone timeZone) : ISlaCalculator
{
    private const int DefaultResponseHours = 9;
    private static readonly TimeSpan DefaultStart = new(9, 0, 0);
    private static readonly TimeSpan DefaultEnd = new(18, 0, 0);

    private readonly ISystemSettings _settings = settings;
    private readonly IEditorTimeZone _timeZone = timeZone;

    public DateTime FirstResponseDueUtc(DateTime receivedUtc)
    {
        var start = ParseTime(_settings.Value("sla.businessHoursStart"), DefaultStart);
        var end = ParseTime(_settings.Value("sla.businessHoursEnd"), DefaultEnd);
        var workingDays = ParseDays(_settings.Value("sla.workingDays"));
        var hoursOwed = ParseHours(_settings.Value("sla.firstResponseHours"), DefaultResponseHours);

        // A working day that is not positive would loop forever below, and a setting nobody checked
        // is exactly how that happens.
        if (end <= start || workingDays.Count == 0)
        {
            return receivedUtc.AddHours(hoursOwed);
        }

        var local = _timeZone.ToEditorLocal(receivedUtc);
        var remaining = TimeSpan.FromHours(hoursOwed);
        var cursor = local;

        // At most a fortnight of days is examined. A company closed for longer than that has a
        // bigger problem than an SLA, and an unbounded loop is not a way to find out.
        for (var guard = 0; guard < 14 && remaining > TimeSpan.Zero; guard++)
        {
            if (!workingDays.Contains(cursor.DayOfWeek))
            {
                cursor = cursor.Date.AddDays(1).Add(start);
                continue;
            }

            var dayOpens = cursor.Date.Add(start);
            var dayCloses = cursor.Date.Add(end);

            if (cursor < dayOpens)
            {
                cursor = dayOpens;
            }

            if (cursor >= dayCloses)
            {
                cursor = cursor.Date.AddDays(1).Add(start);
                continue;
            }

            var availableToday = dayCloses - cursor;

            if (availableToday >= remaining)
            {
                return _timeZone.ToUtc(cursor.Add(remaining));
            }

            remaining -= availableToday;
            cursor = cursor.Date.AddDays(1).Add(start);
        }

        return _timeZone.ToUtc(cursor);
    }

    private static TimeSpan ParseTime(string? value, TimeSpan fallback) =>
        TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static int ParseHours(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;

    private static HashSet<DayOfWeek> ParseDays(string? value)
    {
        var days = new HashSet<DayOfWeek>();

        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<DayOfWeek>(part, ignoreCase: true, out var day))
            {
                days.Add(day);
            }
        }

        if (days.Count == 0)
        {
            days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];
        }

        return days;
    }
}
