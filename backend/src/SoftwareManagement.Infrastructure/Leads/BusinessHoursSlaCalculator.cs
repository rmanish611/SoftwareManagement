using System.Globalization;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Application.Leads;

namespace SoftwareManagement.Infrastructure.Leads;

/// <summary>
/// When a first reply is owed, and how long one actually took.
///
/// Both are counted in the company's working hours rather than in wall-clock hours. An enquiry that
/// arrives at 23:00 on a Saturday is not nine hours late by Sunday morning; it is due nine working
/// hours after the office next opens, and answering it at 10:00 on Monday took one working hour,
/// not thirty-four. A deadline that is already breached before anyone could have read the message
/// teaches people to ignore the deadline, and a duration nobody believes is one nobody acts on
/// (BR-LEAD-10).
/// </summary>
public sealed class BusinessHoursSlaCalculator(
    ISystemSettings settings,
    IEditorTimeZone timeZone,
    IHolidayCalendar holidays) : ISlaCalculator
{
    private const int DefaultResponseHours = 9;

    /// <summary>
    /// A fortnight of days is examined at most. A company closed for longer has a bigger problem
    /// than an SLA, and an unbounded loop is not the way to discover it.
    /// </summary>
    private const int MaximumDaysExamined = 14;

    private static readonly TimeSpan DefaultStart = new(9, 0, 0);
    private static readonly TimeSpan DefaultEnd = new(18, 0, 0);

    private readonly ISystemSettings _settings = settings;
    private readonly IEditorTimeZone _timeZone = timeZone;
    private readonly IHolidayCalendar _holidays = holidays;

    public DateTime FirstResponseDueUtc(DateTime receivedUtc)
    {
        var hours = Hours();
        var owed = TimeSpan.FromHours(ParseHours(_settings.Value("sla.firstResponseHours"), DefaultResponseHours));

        // A working day that is not positive would loop forever below, and a setting nobody checked
        // is exactly how that happens.
        if (!hours.IsUsable)
        {
            return receivedUtc.Add(owed);
        }

        var cursor = _timeZone.ToEditorLocal(receivedUtc);
        var remaining = owed;

        for (var guard = 0; guard < MaximumDaysExamined && remaining > TimeSpan.Zero; guard++)
        {
            if (!IsWorkingDay(cursor, hours))
            {
                cursor = NextMorning(cursor, hours);
                continue;
            }

            var opens = cursor.Date.Add(hours.Start);
            var closes = cursor.Date.Add(hours.End);

            if (cursor < opens)
            {
                cursor = opens;
            }

            if (cursor >= closes)
            {
                cursor = NextMorning(cursor, hours);
                continue;
            }

            var availableToday = closes - cursor;

            if (availableToday >= remaining)
            {
                return _timeZone.ToUtc(cursor.Add(remaining));
            }

            remaining -= availableToday;
            cursor = NextMorning(cursor, hours);
        }

        return _timeZone.ToUtc(cursor);
    }

    public TimeSpan BusinessTimeBetween(DateTime fromUtc, DateTime toUtc)
    {
        if (toUtc <= fromUtc)
        {
            return TimeSpan.Zero;
        }

        var hours = Hours();

        if (!hours.IsUsable)
        {
            return toUtc - fromUtc;
        }

        var start = _timeZone.ToEditorLocal(fromUtc);
        var end = _timeZone.ToEditorLocal(toUtc);
        var elapsed = TimeSpan.Zero;

        // Day by day from the first to the last, adding the part of each working day that falls
        // between the two instants. Summing whole days and adjusting the ends is shorter and gets
        // the holiday and half-open-day cases wrong.
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            if (!IsWorkingDay(day, hours))
            {
                continue;
            }

            var opens = day.Add(hours.Start);
            var closes = day.Add(hours.End);

            var from = start > opens ? start : opens;
            var to = end < closes ? end : closes;

            if (to > from)
            {
                elapsed += to - from;
            }
        }

        return elapsed;
    }

    private bool IsWorkingDay(DateTime local, BusinessHours hours) =>
        hours.Days.Contains(local.DayOfWeek) && !_holidays.IsHoliday(DateOnly.FromDateTime(local));

    private static DateTime NextMorning(DateTime cursor, BusinessHours hours) =>
        cursor.Date.AddDays(1).Add(hours.Start);

    private BusinessHours Hours() => new(
        ParseTime(_settings.Value("sla.businessHoursStart"), DefaultStart),
        ParseTime(_settings.Value("sla.businessHoursEnd"), DefaultEnd),
        ParseDays(_settings.Value("sla.workingDays")));

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

    private sealed record BusinessHours(TimeSpan Start, TimeSpan End, HashSet<DayOfWeek> Days)
    {
        public bool IsUsable => End > Start && Days.Count > 0;
    }
}
