using System.Globalization;
using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Sales;

/// <summary>
/// The next number to hand out, per document kind and financial year.
///
/// A row rather than a computed `MAX(...) + 1`, because two requests reading the same maximum at
/// the same moment both write the same number, and a duplicate quote number is worse than a gap.
/// The allocation takes a row lock inside the caller's transaction, so a number is never handed out
/// twice and never lost if the transaction rolls back (BR-SALE-01, BR-SALE-10).
/// </summary>
public class NumberSequence : AuditableEntity
{
    public const string Quote = "quote";
    public const string Invoice = "invoice";

    public string Key { get; set; } = string.Empty;

    /// <summary>The Indian financial year the sequence belongs to, as `2026-27`.</summary>
    public string FinancialYear { get; set; } = string.Empty;

    public int NextValue { get; set; } = 1;

    /// <summary>
    /// The financial year a date falls in. April to March, which is what "FY" means to everybody
    /// this system bills (A-28).
    /// </summary>
    public static string FinancialYearOf(DateOnly date)
    {
        var startYear = date.Month >= 4 ? date.Year : date.Year - 1;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{startYear}-{(startYear + 1) % 100:D2}");
    }

    /// <summary>`Q/2026-27/00001`. Five digits, because the fifth is reached long before a sixth.</summary>
    public static string Format(string prefix, string financialYear, int value) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}/{financialYear}/{value:D5}");
}
