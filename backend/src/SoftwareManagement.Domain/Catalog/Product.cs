using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Content;

namespace SoftwareManagement.Domain.Catalog;

/// <summary>
/// A software product the company sells: an ERP, a hospital management system, a billing or a
/// school administration product. Each one is a multi-tenant SaaS the customer buys as a tenant on
/// a plan (A-03), not a download.
/// </summary>
public class Product : AuditableEntity, IPublishable
{
    /// <summary>
    /// A product may not be published until it has enough of a page to be worth reading
    /// (BR-CAT-01). These are the thresholds, in one place, so the rule and the message agree.
    /// </summary>
    public const int MinimumFeatures = 3;
    public const int MinimumScreenshots = 1;
    public const int MinimumPlans = 1;

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Tagline { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string? Body { get; set; }

    public Guid CategoryId { get; set; }

    public ProductCategory? Category { get; set; }

    public Guid? HeroAssetId { get; set; }

    public MediaAsset? Hero { get; set; }

    public string? DocsUrl { get; set; }

    public string? RepositoryUrl { get; set; }

    public int SortOrder { get; set; }

    public bool IsFeatured { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public DateTime? PublishedAtUtc { get; set; }

    public DateTime? PublishAtUtc { get; set; }

    public DateTime? UnpublishAtUtc { get; set; }

    public Guid? SeoMetadataId { get; set; }

    public SeoMetadata? Seo { get; set; }

    public ICollection<ProductFeature> Features { get; } = [];

    public ICollection<ProductScreenshot> Screenshots { get; } = [];

    public ICollection<PricingPlan> Plans { get; } = [];

    public ICollection<FaqItem> Faqs { get; } = [];

    public DemoEnvironment? Demo { get; set; }
}

/// <summary>An industry or vertical. Buyers arrive with a job, not a product name (S-10).</summary>
public class ProductCategory : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? IconKey { get; set; }

    public int SortOrder { get; set; }

    public bool IsPublished { get; set; }

    public ICollection<Product> Products { get; } = [];
}

/// <summary>One capability listed on a product page.</summary>
public class ProductFeature : AuditableEntity
{
    public Guid ProductId { get; set; }

    public Product? Product { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Groups features into sections on the page, for example "Billing" or "Reporting".</summary>
    public string? GroupName { get; set; }

    public int SortOrder { get; set; }

    public bool IsHighlighted { get; set; }

    public ICollection<PlanFeature> PlanFeatures { get; } = [];
}

public class ProductScreenshot : AuditableEntity
{
    public Guid ProductId { get; set; }

    public Product? Product { get; set; }

    public Guid MediaAssetId { get; set; }

    public MediaAsset? MediaAsset { get; set; }

    public string? Caption { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// A plan a customer can buy. Prices are stored with an explicit currency, never as a bare number
/// (A-06, BR-CAT-02).
/// </summary>
public class PricingPlan : AuditableEntity
{
    public Guid ProductId { get; set; }

    public Product? Product { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string Currency { get; set; } = "INR";

    public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Monthly;

    public int IncludedSeats { get; set; } = 1;

    public bool IsFreeTier { get; set; }

    /// <summary>
    /// At most one plan per product carries this (BR-CAT-03). Two recommended plans is not a
    /// stronger recommendation, it is no recommendation at all.
    /// </summary>
    public bool IsRecommended { get; set; }

    public decimal SetupFee { get; set; }

    public int SortOrder { get; set; }

    public bool IsPublished { get; set; }

    public ICollection<PlanFeature> PlanFeatures { get; } = [];

    /// <summary>
    /// A yearly plan priced above twelve monthly payments is almost always a typing mistake, and
    /// publishing it would quote a customer more than the monthly plan costs (BR-CAT-04).
    /// </summary>
    public static bool YearlyPriceIsPlausible(decimal yearlyPrice, decimal monthlyPrice) =>
        monthlyPrice <= 0 || yearlyPrice <= monthlyPrice * 12;
}

public enum BillingPeriod
{
    Monthly = 0,
    Quarterly = 1,
    Yearly = 2,
    OneTime = 3,
}

/// <summary>What a feature is worth inside one plan: included, capped at a number, or absent.</summary>
public class PlanFeature : AuditableEntity
{
    public Guid PricingPlanId { get; set; }

    public PricingPlan? PricingPlan { get; set; }

    public Guid ProductFeatureId { get; set; }

    public ProductFeature? ProductFeature { get; set; }

    public FeatureAvailability Availability { get; set; } = FeatureAvailability.Included;

    /// <summary>Required when the availability is Limited: "5 users", "10 GB", "2 branches".</summary>
    public string? LimitValue { get; set; }

    public int SortOrder { get; set; }
}

public enum FeatureAvailability
{
    Included = 0,
    Limited = 1,
    NotIncluded = 2,
}

/// <summary>
/// The live demo for a product. This site links to it and watches whether it is alive; it never
/// hosts or provisions it (A-20).
/// </summary>
public class DemoEnvironment : AuditableEntity
{
    public Guid ProductId { get; set; }

    public Product? Product { get; set; }

    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Shared demo credentials, shown on the published product page only. They are never written to
    /// a log or an email (BR-CAT-06, NFR-PRIV-03).
    /// </summary>
    public string? DemoUsername { get; set; }

    public string? DemoPassword { get; set; }

    public DemoHealth HealthState { get; set; } = DemoHealth.Unknown;

    public DateTime? LastCheckedUtc { get; set; }

    public int ConsecutiveFailures { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Two consecutive failures, not one, before the button disappears (BR-INT-04): a single
    /// timeout is usually the network, and hiding the demo on every blip would be worse than the
    /// occasional slow load.
    /// </summary>
    public const int FailuresBeforeDown = 2;

    public bool ShouldShowPublicly => IsEnabled && HealthState != DemoHealth.Down;
}

public enum DemoHealth
{
    Unknown = 0,
    Up = 1,
    Down = 2,
}

/// <summary>A question and answer, on a product page or across the site.</summary>
public class FaqItem : AuditableEntity
{
    public Guid? ProductId { get; set; }

    public Product? Product { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsPublished { get; set; }
}
