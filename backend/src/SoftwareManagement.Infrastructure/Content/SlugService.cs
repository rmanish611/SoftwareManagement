using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Content;

/// <summary>
/// Slug availability and the redirects a rename leaves behind (BR-SITE-02, BR-SITE-06).
///
/// A slug that has ever been published is never reused, even after the item is archived, because
/// the old URL is still in someone's bookmarks and in search results.
/// </summary>
public sealed class SlugService(AppDbContext dbContext, IClock clock) : ISlugService
{
    private const int MaxRedirectHops = 3;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly IClock _clock = clock;

    public async Task<bool> IsAvailableAsync(string entityType, string slug, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (!Slug.IsValid(slug))
        {
            return false;
        }

        var takenByPage = await _dbContext.Pages
            .AnyAsync(p => p.Slug == slug && (exceptId == null || p.Id != exceptId), cancellationToken)
            .ConfigureAwait(false);

        if (takenByPage)
        {
            return false;
        }

        // A path that already redirects belongs to something that used to live there.
        var path = "/" + slug;
        return !await _dbContext.Redirects.AnyAsync(r => r.FromPath == path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> SuggestAsync(string entityType, string desired, CancellationToken cancellationToken)
    {
        var basis = Slug.From(desired);
        if (basis.Length == 0)
        {
            basis = "page";
        }

        if (await IsAvailableAsync(entityType, basis, null, cancellationToken).ConfigureAwait(false))
        {
            return basis;
        }

        for (var suffix = 2; suffix < 100; suffix++)
        {
            var candidate = $"{basis}-{suffix}";
            if (candidate.Length > Slug.MaxLength)
            {
                candidate = basis[..(Slug.MaxLength - 4)].TrimEnd('-') + "-" + suffix;
            }

            if (await IsAvailableAsync(entityType, candidate, null, cancellationToken).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        return $"{basis}-{Guid.NewGuid():N}"[..Slug.MaxLength];
    }

    public async Task RecordRenameAsync(string oldPath, string newPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(oldPath);
        ArgumentNullException.ThrowIfNull(newPath);

        if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
        {
            return;
        }

        // Repoint everything that already pointed at the old path, so a page renamed twice still
        // costs a visitor exactly one redirect rather than a chain (BR-SITE-06).
        var pointingHere = await _dbContext.Redirects
            .Where(r => r.ToPath == oldPath)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var existing in pointingHere.Take(MaxRedirectHops * 100))
        {
            existing.ToPath = newPath;
        }

        var already = await _dbContext.Redirects
            .FirstOrDefaultAsync(r => r.FromPath == oldPath, cancellationToken).ConfigureAwait(false);

        if (already is not null)
        {
            already.ToPath = newPath;
        }
        else
        {
            _dbContext.Redirects.Add(new Redirect
            {
                Id = Guid.NewGuid(),
                FromPath = oldPath,
                ToPath = newPath,
                StatusCode = 301,
                IsAutomatic = true,
                CreatedBy = "system",
                CreatedAtUtc = _clock.UtcNow,
            });
        }

        // A redirect that points at itself would loop forever (EX-107).
        var selfReferencing = await _dbContext.Redirects
            .Where(r => r.FromPath == r.ToPath)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (selfReferencing.Count > 0)
        {
            _dbContext.Redirects.RemoveRange(selfReferencing);
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Converts between the editor's timezone and UTC (BR-SITE-04).
///
/// Windows time zone ids are used because the application targets Windows and SQL Server; the
/// lookup falls back to the IANA id so a Linux deployment keeps working.
/// </summary>
public sealed class EditorTimeZone(IClock clock) : IEditorTimeZone
{
    private readonly IClock _clock = clock;

    public string TimeZoneId { get; set; } = "India Standard Time";

    public DateTime ToUtc(DateTime localDateTime) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified), Zone());

    public DateTime ToEditorLocal(DateTime utcDateTime) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), Zone());

    public DateTime ToServerLocal(DateTime utcDateTime) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), TimeZoneInfo.Local);

    private TimeZoneInfo Zone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // A machine that does not know the Windows id may know the IANA one.
            return TimeZoneId switch
            {
                "India Standard Time" => TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"),
                _ => TimeZoneInfo.Utc,
            };
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>The current instant, in the editor's timezone. Used by the scheduling dialog.</summary>
    public DateTime NowForEditor() => ToEditorLocal(_clock.UtcNow);
}
