using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Content;

/// <summary>A public page: home, about, services, contact, privacy, terms or a custom one.</summary>
public class Page : AuditableEntity, IPublishable
{
    public string Title { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public PageType PageType { get; set; } = PageType.Custom;

    /// <summary>A system page is part of the site's structure and cannot be deleted, only edited.</summary>
    public bool IsSystemPage { get; set; }

    public string? Body { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public DateTime? PublishedAtUtc { get; set; }

    public DateTime? PublishAtUtc { get; set; }

    public DateTime? UnpublishAtUtc { get; set; }

    public Guid? SeoMetadataId { get; set; }

    public SeoMetadata? Seo { get; set; }

    public ICollection<PageSection> Sections { get; } = [];
}

public enum PageType
{
    Custom = 0,
    Home = 1,
    About = 2,
    Services = 3,
    Contact = 4,
    Legal = 5,
}

/// <summary>An ordered block on a page. Sections are what make a page editable without HTML.</summary>
public class PageSection : AuditableEntity
{
    public Guid PageId { get; set; }

    public Page? Page { get; set; }

    public SectionType SectionType { get; set; } = SectionType.RichText;

    public string? Heading { get; set; }

    public string? Body { get; set; }

    public Guid? MediaAssetId { get; set; }

    public MediaAsset? MediaAsset { get; set; }

    public int SortOrder { get; set; }

    public bool IsVisible { get; set; } = true;
}

public enum SectionType
{
    RichText = 0,
    Hero = 1,
    FeatureGrid = 2,
    CallToAction = 3,
    Testimonials = 4,
    LogoWall = 5,
}
