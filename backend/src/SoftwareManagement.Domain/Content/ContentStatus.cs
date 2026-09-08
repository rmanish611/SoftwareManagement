namespace SoftwareManagement.Domain.Content;

/// <summary>
/// The publishing state of any content item (BR-SITE-01).
///
/// <c>Modified</c> is the state most systems leave out and then regret: without it, editing a live
/// page either publishes half-finished text immediately or forces the editor to take the page down
/// first. With it, the public keeps seeing the last published version while the draft is worked on.
/// </summary>
public enum ContentStatus
{
    /// <summary>Never published. Invisible to the public, and a request for its URL is a 404.</summary>
    Draft = 0,

    /// <summary>Live, with no unpublished changes.</summary>
    Published = 1,

    /// <summary>Live, but the draft has edits that have not been published yet.</summary>
    Modified = 2,

    /// <summary>Was live and has been taken down. The URL is a 404 again.</summary>
    Unpublished = 3,

    /// <summary>Retired. Never listed, never published again, and its slug is never reused.</summary>
    Archived = 4,
}

/// <summary>
/// The publishing behaviour every content type shares. Implemented by Page, Product, Project and
/// ApiCatalogEntry so one publishing service, one scheduler and one version writer serve them all.
/// </summary>
public interface IPublishable
{
    Guid Id { get; }

    string Slug { get; set; }

    ContentStatus Status { get; set; }

    DateTime? PublishedAtUtc { get; set; }

    DateTime? PublishAtUtc { get; set; }

    DateTime? UnpublishAtUtc { get; set; }

    /// <summary>What the public sees. A Modified item still serves its last published version.</summary>
    bool IsPubliclyVisible => Status is ContentStatus.Published or ContentStatus.Modified;
}
