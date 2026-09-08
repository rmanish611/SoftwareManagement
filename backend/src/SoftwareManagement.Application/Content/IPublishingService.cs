using SoftwareManagement.Domain.Content;

namespace SoftwareManagement.Application.Content;

/// <summary>
/// The publishing rules, in one place, for every content type.
///
/// They are here rather than in each controller because the same four mistakes are possible on
/// every type: publishing something that points at unpublished content, scheduling into the past,
/// losing the previous version, and leaving a dead URL behind after a rename.
/// </summary>
public interface IPublishingService
{
    /// <summary>
    /// Publishes now. Refuses with the offending references listed when the item points at
    /// anything unpublished or archived (BR-SITE-03), and writes a content version on success.
    /// </summary>
    Task<PublishResult> PublishPageAsync(Guid pageId, string actorEmail, CancellationToken cancellationToken);

    Task<PublishResult> UnpublishPageAsync(Guid pageId, string actorEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Schedules a publish, and optionally an unpublish. Times arrive as instants already converted
    /// from the editor's timezone by the caller; this method enforces the window rules (BR-SITE-05).
    /// </summary>
    Task<PublishResult> SchedulePageAsync(
        Guid pageId,
        DateTime publishAtUtc,
        DateTime? unpublishAtUtc,
        string actorEmail,
        CancellationToken cancellationToken);

    /// <summary>Publishes several items, reporting each failure rather than stopping at the first.</summary>
    Task<IReadOnlyList<BulkPublishOutcome>> PublishManyAsync(
        IReadOnlyList<Guid> pageIds,
        string actorEmail,
        CancellationToken cancellationToken);

    /// <summary>Restores an earlier version as a new version. Never deletes history (BR-SITE-07).</summary>
    Task<PublishResult> RestoreVersionAsync(
        Guid pageId,
        int versionNumber,
        string actorEmail,
        CancellationToken cancellationToken);

    /// <summary>Runs due scheduled transitions. Called by the background scheduler.</summary>
    Task<int> ApplyDueScheduledTransitionsAsync(CancellationToken cancellationToken);
}

public sealed record PublishResult(PublishOutcome Outcome, IReadOnlyList<string> BlockingReferences)
{
    public bool Succeeded => Outcome == PublishOutcome.Published || Outcome == PublishOutcome.Unpublished
        || Outcome == PublishOutcome.Scheduled || Outcome == PublishOutcome.Restored;

    public static PublishResult Ok(PublishOutcome outcome) => new(outcome, []);

    public static PublishResult Blocked(IReadOnlyList<string> references) =>
        new(PublishOutcome.BlockedByUnpublishedReferences, references);

    public static PublishResult Failed(PublishOutcome outcome) => new(outcome, []);
}

public enum PublishOutcome
{
    Published = 0,
    Unpublished = 1,
    Scheduled = 2,
    Restored = 3,
    NotFound = 4,
    BlockedByUnpublishedReferences = 5,
    ScheduleTooSoon = 6,
    ScheduleTooFar = 7,
    UnpublishBeforePublish = 8,
    VersionNotFound = 9,
    VersionReferencesMissingContent = 10,
}

/// <summary>
/// The result for one item in a bulk publish. <c>Outcome</c> carries the enum name rather than its
/// number: a client that switched on the number would break the first time a value was inserted
/// into the middle of the enum.
/// </summary>
public sealed record BulkPublishOutcome(Guid Id, string Title, string Outcome, IReadOnlyList<string> BlockingReferences);

/// <summary>
/// Slug rules and the redirect that must follow a rename (BR-SITE-06). Separate from publishing
/// because a slug can change on a draft too, where no redirect is needed.
/// </summary>
public interface ISlugService
{
    Task<bool> IsAvailableAsync(string entityType, string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Returns the first free variation, for example `erp-2`, when the slug is taken.</summary>
    Task<string> SuggestAsync(string entityType, string desired, CancellationToken cancellationToken);

    /// <summary>
    /// Records the 301 a renamed published item needs, and collapses a chain so an old path never
    /// takes more than one hop (BR-SITE-06).
    /// </summary>
    Task RecordRenameAsync(string oldPath, string newPath, CancellationToken cancellationToken);
}

/// <summary>Scheduling boundaries from BR-SITE-05, in one place so the API and the tests agree.</summary>
public static class ScheduleRules
{
    public static readonly TimeSpan MinimumLeadTime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaximumLeadTime = TimeSpan.FromDays(365);

    public static PublishOutcome? Validate(DateTime nowUtc, DateTime publishAtUtc, DateTime? unpublishAtUtc)
    {
        if (publishAtUtc - nowUtc < MinimumLeadTime)
        {
            return PublishOutcome.ScheduleTooSoon;
        }

        if (publishAtUtc - nowUtc > MaximumLeadTime)
        {
            return PublishOutcome.ScheduleTooFar;
        }

        if (unpublishAtUtc is not null && unpublishAtUtc <= publishAtUtc)
        {
            return PublishOutcome.UnpublishBeforePublish;
        }

        return null;
    }
}

/// <summary>
/// Turns an instant the editor entered in their own timezone into UTC, and back for display
/// (BR-SITE-04, S-06). The editor's timezone is the one that matters: scheduling "12 PM" must mean
/// noon where the person is, not noon on a server they will never see.
/// </summary>
public interface IEditorTimeZone
{
    /// <summary>The Windows time zone id for the current editor, defaulting to India Standard Time.</summary>
    string TimeZoneId { get; }

    DateTime ToUtc(DateTime localDateTime);

    DateTime ToEditorLocal(DateTime utcDateTime);

    /// <summary>
    /// The same instant expressed in the server's own timezone, which the scheduling dialog shows
    /// so an editor in a different zone is never surprised by when the item actually appears.
    /// </summary>
    DateTime ToServerLocal(DateTime utcDateTime);
}
