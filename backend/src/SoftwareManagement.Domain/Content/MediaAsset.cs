using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Content;

/// <summary>
/// An uploaded file. The row is the record; the bytes live behind an IFileStorage abstraction so
/// moving to cloud storage later is one class, not a migration (A-19).
/// </summary>
public class MediaAsset : AuditableEntity
{
    public const long MaxImageBytes = 10 * 1024 * 1024;
    public const long MaxDocumentBytes = 25 * 1024 * 1024;

    /// <summary>The name the person uploaded it under. Never used as a path.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Where the bytes live. Generated, never taken from the upload.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Determined by inspecting the bytes, never trusted from the client (NFR-SEC-06).</summary>
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public string? AltText { get; set; }

    /// <summary>Lets a re-upload of the same file reuse the stored bytes instead of duplicating them.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public MediaKind Kind { get; set; } = MediaKind.Image;

    /// <summary>The resized web version, when one was produced. Null for documents.</summary>
    public string? WebStorageKey { get; set; }

    public string? ThumbnailStorageKey { get; set; }
}

public enum MediaKind
{
    Image = 0,
    Document = 1,
    Logo = 2,
}

/// <summary>A service the company sells.</summary>
public class Service : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string? Body { get; set; }

    public string? IconKey { get; set; }

    public int SortOrder { get; set; }

    public bool IsPublished { get; set; }

    public ICollection<ServiceTechnology> ServiceTechnologies { get; } = [];
}

/// <summary>A technology in the stack, shown on the services and about pages.</summary>
public class Technology : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public TechnologyCategory Category { get; set; } = TechnologyCategory.Framework;

    public Guid? LogoAssetId { get; set; }

    public MediaAsset? Logo { get; set; }

    /// <summary>1 to 5. Optional, because claiming a number for everything is not credible.</summary>
    public byte? Proficiency { get; set; }

    public int SortOrder { get; set; }

    public ICollection<ServiceTechnology> ServiceTechnologies { get; } = [];
}

public enum TechnologyCategory
{
    Language = 0,
    Framework = 1,
    Database = 2,
    Cloud = 3,
    Tool = 4,
}

public class ServiceTechnology
{
    public Guid ServiceId { get; set; }

    public Service? Service { get; set; }

    public Guid TechnologyId { get; set; }

    public Technology? Technology { get; set; }
}

/// <summary>Someone on the about page.</summary>
public class TeamMember : AuditableEntity
{
    public string FullName { get; set; } = string.Empty;

    public string RoleTitle { get; set; } = string.Empty;

    public string? Bio { get; set; }

    public Guid? PhotoAssetId { get; set; }

    public MediaAsset? Photo { get; set; }

    public string? LinkedInUrl { get; set; }

    public bool IsPublic { get; set; } = true;

    public int SortOrder { get; set; }
}

/// <summary>
/// A customer's words. Never published without an explicit permission flag (BR-PRJ-03): quoting a
/// client who did not agree to be quoted is a real-world problem, not a hypothetical one.
/// </summary>
public class Testimonial : AuditableEntity
{
    public string AuthorName { get; set; } = string.Empty;

    public string AuthorRole { get; set; } = string.Empty;

    public string? OrganisationName { get; set; }

    public string Quote { get; set; } = string.Empty;

    public bool HasPermission { get; set; }

    public bool IsPublished { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>An entry in the header or footer menu.</summary>
public class NavigationItem : AuditableEntity
{
    public NavigationMenu Menu { get; set; } = NavigationMenu.Header;

    public string Label { get; set; } = string.Empty;

    public Guid? PageId { get; set; }

    public Page? Page { get; set; }

    public string? ExternalUrl { get; set; }

    public Guid? ParentId { get; set; }

    public NavigationItem? Parent { get; set; }

    public int SortOrder { get; set; }

    public bool OpensInNewTab { get; set; }
}

public enum NavigationMenu
{
    Header = 0,
    Footer = 1,
}

/// <summary>A time-boxed banner across the top of the site.</summary>
public class Announcement : AuditableEntity
{
    public string Message { get; set; } = string.Empty;

    public string? LinkUrl { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public AnnouncementSeverity Severity { get; set; } = AnnouncementSeverity.Information;

    public bool IsLiveAt(DateTime nowUtc) => StartsAtUtc <= nowUtc && EndsAtUtc > nowUtc;
}

public enum AnnouncementSeverity
{
    Information = 0,
    Success = 1,
    Warning = 2,
}
