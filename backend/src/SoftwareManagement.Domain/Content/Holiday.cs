using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Content;

/// <summary>
/// A day the office is shut (E-55).
///
/// The SLA clock reads this table, so an enquiry arriving the evening before Diwali is not overdue
/// by the time anyone is back (BR-LEAD-10). The owner maintains the list; the screens for that come
/// with the rest of the settings in P13, and until then it is seeded and read.
/// </summary>
public class Holiday : AuditableEntity
{
    public DateOnly HolidayDate { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether the same day and month is a holiday every year. Republic Day is; Diwali is not,
    /// because it moves, and a recurring row for it would close the office on the wrong day.
    /// </summary>
    public bool IsRecurring { get; set; }

    public bool Falls(DateOnly date) =>
        IsRecurring
            ? HolidayDate.Month == date.Month && HolidayDate.Day == date.Day
            : HolidayDate == date;
}
