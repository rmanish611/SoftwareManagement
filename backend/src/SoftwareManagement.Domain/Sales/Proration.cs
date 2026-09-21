namespace SoftwareManagement.Domain.Sales;

/// <summary>
/// What a mid-term change costs.
///
/// The formula is the one REQ-SALE-015 states in words: the new price for the days remaining, less
/// the unused part of what was already paid. Twenty days left of thirty, moving from 3,000 to
/// 5,000, is 5000 x 20/30 - 3000 x 20/30 = 1,333.33.
///
/// It lives here rather than inside the subscription service because it is arithmetic with one
/// right answer, and a test that can state the answer without a database is worth more than one
/// that has to build a subscription to ask.
/// </summary>
public static class Proration
{
    /// <summary>
    /// The charge for moving to <paramref name="newPeriodAmount"/> with
    /// <paramref name="daysRemaining"/> of a <paramref name="daysInPeriod"/> period left.
    ///
    /// Never negative: a downgrade takes effect at renewal rather than refunding mid-term
    /// (BR-SALE-11), so the caller gets zero and applies the change to the next period.
    /// </summary>
    public static decimal Charge(
        decimal oldPeriodAmount, decimal newPeriodAmount, int daysRemaining, int daysInPeriod)
    {
        if (daysInPeriod <= 0 || daysRemaining <= 0)
        {
            return 0m;
        }

        var used = Math.Min(daysRemaining, daysInPeriod);
        var share = (decimal)used / daysInPeriod;

        // Each side is rounded before subtracting, so the charge is the difference between two
        // figures that could each appear on an invoice - not the rounding of a difference nobody
        // can reconstruct (BR-SALE-03).
        var owedForNew = QuoteLineItem.Round(newPeriodAmount * share);
        var unusedOnOld = QuoteLineItem.Round(oldPeriodAmount * share);

        var charge = owedForNew - unusedOnOld;
        return charge > 0m ? charge : 0m;
    }

    /// <summary>Whole days from one date up to but not including another; never negative.</summary>
    public static int DaysBetween(DateOnly from, DateOnly to) =>
        to > from ? to.DayNumber - from.DayNumber : 0;
}
