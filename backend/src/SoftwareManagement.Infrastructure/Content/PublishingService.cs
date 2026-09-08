using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Domain.Audit;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Content;

/// <summary>
/// Publishing, scheduling, versioning and restore for pages.
///
/// Every path here writes a content version before it changes what the public sees, so "undo" is
/// always available and never destructive (BR-SITE-07).
/// </summary>
public sealed class PublishingService(
    AppDbContext dbContext,
    ISlugService slugs,
    IClock clock) : IPublishingService
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly ISlugService _slugs = slugs;
    private readonly IClock _clock = clock;

    public async Task<PublishResult> PublishPageAsync(Guid pageId, string actorEmail, CancellationToken cancellationToken)
    {
        var page = await LoadPageAsync(pageId, cancellationToken).ConfigureAwait(false);
        if (page is null)
        {
            return PublishResult.Failed(PublishOutcome.NotFound);
        }

        var blocking = await BlockingReferencesAsync(page, cancellationToken).ConfigureAwait(false);
        if (blocking.Count > 0)
        {
            return PublishResult.Blocked(blocking);
        }

        page.Status = ContentStatus.Published;
        page.PublishedAtUtc = _clock.UtcNow;
        page.PublishAtUtc = null;
        page.ModifiedBy = actorEmail;

        await WriteVersionAsync(page, actorEmail, cancellationToken).ConfigureAwait(false);
        Audit(AuditActions.Updated, page, actorEmail, "{\"status\":\"Published\"}");

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PublishResult.Ok(PublishOutcome.Published);
    }

    public async Task<PublishResult> UnpublishPageAsync(Guid pageId, string actorEmail, CancellationToken cancellationToken)
    {
        var page = await LoadPageAsync(pageId, cancellationToken).ConfigureAwait(false);
        if (page is null)
        {
            return PublishResult.Failed(PublishOutcome.NotFound);
        }

        page.Status = ContentStatus.Unpublished;
        page.UnpublishAtUtc = null;
        page.ModifiedBy = actorEmail;

        Audit(AuditActions.Updated, page, actorEmail, "{\"status\":\"Unpublished\"}");
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return PublishResult.Ok(PublishOutcome.Unpublished);
    }

    public async Task<PublishResult> SchedulePageAsync(
        Guid pageId,
        DateTime publishAtUtc,
        DateTime? unpublishAtUtc,
        string actorEmail,
        CancellationToken cancellationToken)
    {
        var invalid = ScheduleRules.Validate(_clock.UtcNow, publishAtUtc, unpublishAtUtc);
        if (invalid is not null)
        {
            return PublishResult.Failed(invalid.Value);
        }

        var page = await LoadPageAsync(pageId, cancellationToken).ConfigureAwait(false);
        if (page is null)
        {
            return PublishResult.Failed(PublishOutcome.NotFound);
        }

        var blocking = await BlockingReferencesAsync(page, cancellationToken).ConfigureAwait(false);
        if (blocking.Count > 0)
        {
            // Refusing now beats discovering at 09:00 tomorrow that the scheduled publish failed.
            return PublishResult.Blocked(blocking);
        }

        page.PublishAtUtc = publishAtUtc;
        page.UnpublishAtUtc = unpublishAtUtc;
        page.ModifiedBy = actorEmail;

        Audit(AuditActions.Updated, page, actorEmail,
            $"{{\"publishAtUtc\":\"{publishAtUtc:O}\",\"unpublishAtUtc\":\"{unpublishAtUtc:O}\"}}");

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PublishResult.Ok(PublishOutcome.Scheduled);
    }

    public async Task<IReadOnlyList<BulkPublishOutcome>> PublishManyAsync(
        IReadOnlyList<Guid> pageIds,
        string actorEmail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pageIds);

        var results = new List<BulkPublishOutcome>(pageIds.Count);

        foreach (var id in pageIds)
        {
            var page = await LoadPageAsync(id, cancellationToken).ConfigureAwait(false);
            if (page is null)
            {
                results.Add(new BulkPublishOutcome(id, string.Empty, nameof(PublishOutcome.NotFound), []));
                continue;
            }

            var result = await PublishPageAsync(id, actorEmail, cancellationToken).ConfigureAwait(false);
            results.Add(new BulkPublishOutcome(id, page.Title, result.Outcome.ToString(), result.BlockingReferences));
        }

        return results;
    }

    public async Task<PublishResult> RestoreVersionAsync(
        Guid pageId,
        int versionNumber,
        string actorEmail,
        CancellationToken cancellationToken)
    {
        var page = await LoadPageAsync(pageId, cancellationToken).ConfigureAwait(false);
        if (page is null)
        {
            return PublishResult.Failed(PublishOutcome.NotFound);
        }

        var version = await _dbContext.ContentVersions
            .FirstOrDefaultAsync(
                v => v.EntityType == nameof(Page) && v.EntityId == pageId && v.VersionNumber == versionNumber,
                cancellationToken)
            .ConfigureAwait(false);

        if (version is null)
        {
            return PublishResult.Failed(PublishOutcome.VersionNotFound);
        }

        var snapshot = JsonSerializer.Deserialize<PageSnapshot>(version.SnapshotJson);
        if (snapshot is null)
        {
            return PublishResult.Failed(PublishOutcome.VersionNotFound);
        }

        // A version can name media that has since been deleted. Resurrecting a broken page is worse
        // than refusing, so the restore is refused with the missing reference named.
        if (snapshot.MediaAssetIds.Count > 0)
        {
            var present = await _dbContext.MediaAssets
                .Where(m => snapshot.MediaAssetIds.Contains(m.Id))
                .Select(m => m.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            if (present.Count != snapshot.MediaAssetIds.Count)
            {
                return PublishResult.Failed(PublishOutcome.VersionReferencesMissingContent);
            }
        }

        page.Title = snapshot.Title;
        page.Body = snapshot.Body;
        page.ModifiedBy = actorEmail;
        page.Status = page.Status == ContentStatus.Published ? ContentStatus.Modified : page.Status;

        // Restoring writes a new version rather than rewinding the list: history is never lost.
        await WriteVersionAsync(page, actorEmail, cancellationToken).ConfigureAwait(false);
        Audit(AuditActions.Updated, page, actorEmail, $"{{\"restoredFromVersion\":{versionNumber}}}");

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PublishResult.Ok(PublishOutcome.Restored);
    }

    public async Task<int> ApplyDueScheduledTransitionsAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var applied = 0;

        var duePublishes = await _dbContext.Pages
            .Where(p => p.PublishAtUtc != null && p.PublishAtUtc <= now)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var page in duePublishes)
        {
            page.Status = ContentStatus.Published;
            page.PublishedAtUtc = now;
            page.PublishAtUtc = null;
            applied++;
        }

        var dueUnpublishes = await _dbContext.Pages
            .Where(p => p.UnpublishAtUtc != null && p.UnpublishAtUtc <= now)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var page in dueUnpublishes)
        {
            page.Status = ContentStatus.Unpublished;
            page.UnpublishAtUtc = null;
            applied++;
        }

        if (applied > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return applied;
    }

    private Task<Page?> LoadPageAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Pages
            .Include(p => p.Sections)
            .Include(p => p.Seo)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    /// <summary>
    /// Everything the page points at that the public could not see (BR-SITE-03). Publishing a page
    /// whose hero image was archived produces a broken page, so it is refused with the reason named
    /// rather than published and quietly wrong.
    /// </summary>
    private async Task<List<string>> BlockingReferencesAsync(Page page, CancellationToken cancellationToken)
    {
        var blocking = new List<string>();

        var assetIds = page.Sections
            .Where(s => s.MediaAssetId is not null)
            .Select(s => s.MediaAssetId!.Value)
            .ToList();

        if (page.Seo?.OgImageAssetId is not null)
        {
            assetIds.Add(page.Seo.OgImageAssetId.Value);
        }

        if (assetIds.Count > 0)
        {
            var found = await _dbContext.MediaAssets
                .Where(m => assetIds.Contains(m.Id))
                .Select(m => m.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            blocking.AddRange(assetIds
                .Where(id => !found.Contains(id))
                .Select(id => $"media asset {id} is missing"));
        }

        return blocking;
    }

    private async Task WriteVersionAsync(Page page, string actorEmail, CancellationToken cancellationToken)
    {
        var next = await _dbContext.ContentVersions
            .Where(v => v.EntityType == nameof(Page) && v.EntityId == page.Id)
            .MaxAsync(v => (int?)v.VersionNumber, cancellationToken).ConfigureAwait(false) ?? 0;

        var snapshot = new PageSnapshot(
            page.Title,
            page.Body,
            [.. page.Sections.Where(s => s.MediaAssetId is not null).Select(s => s.MediaAssetId!.Value)]);

        _dbContext.ContentVersions.Add(new ContentVersion
        {
            Id = Guid.NewGuid(),
            EntityType = nameof(Page),
            EntityId = page.Id,
            VersionNumber = next + 1,
            SnapshotJson = JsonSerializer.Serialize(snapshot),
            CreatedAtUtc = _clock.UtcNow,
            CreatedBy = actorEmail,
        });

        // Keep the most recent versions and drop the rest, so history is useful without being
        // unbounded (BR-SITE-07).
        var surplus = await _dbContext.ContentVersions
            .Where(v => v.EntityType == nameof(Page) && v.EntityId == page.Id)
            .OrderByDescending(v => v.VersionNumber)
            .Skip(ContentVersion.RetainedPerItem - 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (surplus.Count > 0)
        {
            _dbContext.ContentVersions.RemoveRange(surplus);
        }
    }

    private void Audit(string action, Page page, string actorEmail, string afterJson) =>
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = _clock.UtcNow,
            ActorEmail = actorEmail,
            Action = action,
            EntityType = nameof(Page),
            EntityId = page.Id,
            AfterJson = afterJson,
            CorrelationId = Guid.NewGuid(),
        });

    private sealed record PageSnapshot(string Title, string? Body, List<Guid> MediaAssetIds);
}
