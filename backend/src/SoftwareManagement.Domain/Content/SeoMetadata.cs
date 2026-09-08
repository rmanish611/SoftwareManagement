using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Content;

/// <summary>
/// What a search engine and a social preview show for one content item (BR-SITE-09).
///
/// The length guidance is a warning, not a rule: search engines truncate a long title, they do not
/// reject the page, so refusing to publish over it would be the software inventing a problem.
/// </summary>
public class SeoMetadata : AuditableEntity
{
    public const int TitleSoftMax = 60;
    public const int DescriptionSoftMin = 50;
    public const int DescriptionSoftMax = 160;

    public string MetaTitle { get; set; } = string.Empty;

    public string? MetaDescription { get; set; }

    public string? CanonicalUrl { get; set; }

    public Guid? OgImageAssetId { get; set; }

    public MediaAsset? OgImage { get; set; }

    /// <summary>Keeps a page out of search results without unpublishing it.</summary>
    public bool NoIndex { get; set; }

    public string? JsonLdOverride { get; set; }

    public bool TitleIsWithinGuidance => MetaTitle.Length is > 0 and <= TitleSoftMax;

    public bool DescriptionIsWithinGuidance =>
        MetaDescription is null
        || MetaDescription.Length is >= DescriptionSoftMin and <= DescriptionSoftMax;
}

/// <summary>
/// A permanent redirect from a path that used to work (BR-SITE-06). Created automatically whenever
/// a published item's slug changes, because a marketing site that answers 404 to a link someone
/// already published has lost the visitor for nothing.
/// </summary>
public class Redirect : AuditableEntity
{
    public string FromPath { get; set; } = string.Empty;

    public string ToPath { get; set; } = string.Empty;

    public short StatusCode { get; set; } = 301;

    public bool IsAutomatic { get; set; } = true;

    public int HitCount { get; set; }

    public DateTime? LastHitUtc { get; set; }
}

/// <summary>
/// An immutable snapshot written on every publish (BR-SITE-07). Restoring one creates a new version
/// rather than deleting history, so "undo" never destroys the thing it is undoing.
/// </summary>
public class ContentVersion
{
    public const int RetainedPerItem = 20;

    public Guid Id { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public int VersionNumber { get; set; }

    public string SnapshotJson { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public Guid? PublishedByUserId { get; set; }
}
