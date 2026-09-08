namespace SoftwareManagement.Domain.Catalog;

/// <summary>
/// Whether a product has enough of a page to be worth a visitor's time (BR-CAT-01).
///
/// The thresholds are here, once, because three places need the same answer: the API refuses a
/// publish below them, the admin screen tells an editor what is still missing, and the tests assert
/// the exact counts. A second copy of these numbers would eventually disagree with the first.
/// </summary>
public static class ProductReadiness
{
    public static ProductReadinessResult Evaluate(
        int featureCount,
        int screenshotCount,
        int planCount,
        bool categoryIsPublished)
    {
        var shortfalls = new List<ProductShortfall>();

        if (featureCount < Product.MinimumFeatures)
        {
            shortfalls.Add(new ProductShortfall("features", featureCount, Product.MinimumFeatures));
        }

        if (screenshotCount < Product.MinimumScreenshots)
        {
            shortfalls.Add(new ProductShortfall("screenshots", screenshotCount, Product.MinimumScreenshots));
        }

        if (planCount < Product.MinimumPlans)
        {
            shortfalls.Add(new ProductShortfall("pricing plans", planCount, Product.MinimumPlans));
        }

        // A product in an unpublished category would be live at its own address and absent from
        // every route a visitor could use to find it, which is worse than not publishing it.
        if (!categoryIsPublished)
        {
            shortfalls.Add(new ProductShortfall("a published category", 0, 1));
        }

        return new ProductReadinessResult(shortfalls);
    }
}

public sealed record ProductShortfall(string What, int Has, int Needs)
{
    public override string ToString() => $"{What}: has {Has}, needs {Needs}";
}

public sealed record ProductReadinessResult(IReadOnlyList<ProductShortfall> Shortfalls)
{
    public bool IsReady => Shortfalls.Count == 0;

    /// <summary>The failing counts in one sentence, which is what the 422 response carries.</summary>
    public string Explain() =>
        IsReady
            ? "This product is ready to publish."
            : "Not enough to publish yet - " + string.Join("; ", Shortfalls.Select(s => s.ToString())) + ".";
}
