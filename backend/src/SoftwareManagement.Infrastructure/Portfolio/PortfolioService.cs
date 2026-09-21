using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Portfolio;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Domain.Portfolio;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Portfolio;

/// <summary>
/// The publishing gate for the portfolio and the developer directory.
///
/// Both halves refuse for the same kind of reason: not that a form was filled in wrongly, but that
/// publishing would tell somebody outside the company something untrue or unusable - an outcome
/// with no number behind it, a client named who never agreed, a version withdrawn with no notice.
/// </summary>
public sealed class PortfolioService(AppDbContext dbContext, IClock clock) : IPortfolioService
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IClock _clock = clock;

    public async Task<PortfolioResult> PublishProjectAsync(Guid projectId, string actor, CancellationToken cancellationToken)
    {
        var project = await _dbContext.Projects
            .Include(p => p.CaseStudy!).ThenInclude(c => c.Testimonial)
            .Include(p => p.ClientLogo)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken).ConfigureAwait(false);

        if (project is null)
        {
            return PortfolioResult.NotFound();
        }

        if (project.CaseStudy is { } study)
        {
            var shortfalls = study.PublishingShortfalls();

            if (shortfalls.Count > 0)
            {
                // A case study that says "significantly improved" and nothing else is a page nobody
                // believes, so the metric is required rather than encouraged (BR-PRJ-02).
                return PortfolioResult.Refused(
                    "CASE_STUDY_NOT_READY",
                    "caseStudy",
                    "The case study is missing " + string.Join(", ", shortfalls) + ".",
                    shortfalls);
            }

            // A quote is somebody's words with their name on them. Publishing one they have not
            // agreed to, or one with nobody behind it, is refused here rather than silently dropped
            // from the page, because the editor who attached it needs to know (BR-PRJ-03).
            if (study.Testimonial is { } quote)
            {
                if (!quote.HasPermission)
                {
                    return PortfolioResult.Refused(
                        "TESTIMONIAL_NOT_PERMITTED",
                        "testimonialId",
                        $"{quote.AuthorName} has not agreed to be quoted. Record their permission, or detach the quote.");
                }

                if (string.IsNullOrWhiteSpace(quote.AuthorName) || string.IsNullOrWhiteSpace(quote.AuthorRole))
                {
                    return PortfolioResult.Refused(
                        "TESTIMONIAL_ANONYMOUS",
                        "testimonialId",
                        "An unattributed quote persuades nobody. Give the author's name and role, or detach it.");
                }
            }
        }

        // A logo without permission does not block publishing - the page renders the anonymised
        // label instead, which is the answer the client agreed to (BR-PRJ-01). What is refused is
        // naming them in the project's own field while their logo says permission was not given.
        if (project.ClientLogo is { HasPermission: false } && !string.IsNullOrWhiteSpace(project.ClientDisplayName))
        {
            return PortfolioResult.Refused(
                "CLIENT_NOT_PERMITTED",
                "clientDisplayName",
                $"{project.ClientLogo.DisplayName} has not agreed to be named. Clear the client name, or record their permission.");
        }

        project.Status = ContentStatus.Published;
        project.PublishedAtUtc = _clock.UtcNow;
        project.ModifiedBy = actor;

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PortfolioResult.Done();
    }

    public async Task<PortfolioResult> PublishApiEntryAsync(Guid entryId, string actor, CancellationToken cancellationToken)
    {
        var entry = await _dbContext.ApiCatalogEntries
            .Include(a => a.Versions)
            .FirstOrDefaultAsync(a => a.Id == entryId, cancellationToken).ConfigureAwait(false);

        if (entry is null)
        {
            return PortfolioResult.NotFound();
        }

        var shortfalls = entry.PublishingShortfalls();

        if (shortfalls.Count > 0)
        {
            return PortfolioResult.Refused(
                "API_NOT_READY",
                "versions",
                "This entry needs " + string.Join(", ", shortfalls) + ".",
                shortfalls);
        }

        entry.Status = ContentStatus.Published;
        entry.PublishedAtUtc = _clock.UtcNow;
        entry.ModifiedBy = actor;

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PortfolioResult.Done();
    }

    public async Task<PortfolioResult> MakeCurrentAsync(Guid versionId, string actor, CancellationToken cancellationToken)
    {
        var version = await _dbContext.ApiVersions
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken).ConfigureAwait(false);

        if (version is null)
        {
            return PortfolioResult.NotFound();
        }

        if (version.Status == ApiVersionStatus.Deprecated)
        {
            // Telling a developer to build against something being withdrawn is worse than telling
            // them nothing (BR-API-02).
            return PortfolioResult.Refused(
                "VERSION_DEPRECATED", "status", "A deprecated version cannot be the one to build against.");
        }

        var siblings = await _dbContext.ApiVersions
            .Where(v => v.ApiCatalogEntryId == version.ApiCatalogEntryId && v.IsCurrent && v.Id != versionId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Two saves, not one. The filtered unique index allows a single current version per entry,
        // and EF gives no order to the UPDATEs inside one SaveChanges - so setting the new flag
        // before the old one is cleared is a coin toss that lands on a unique-key violation about
        // half the time. Clearing first and setting second makes the order explicit, and the
        // transaction means the entry is never left with no version to build against.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            foreach (var sibling in siblings)
            {
                sibling.IsCurrent = false;
                sibling.ModifiedBy = actor;
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            version.IsCurrent = true;
            version.ModifiedBy = actor;

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return PortfolioResult.Done();
    }

    public async Task<PortfolioResult> DeprecateAsync(
        Guid versionId, DateOnly sunsetDate, string actor, CancellationToken cancellationToken)
    {
        var version = await _dbContext.ApiVersions
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken).ConfigureAwait(false);

        if (version is null)
        {
            return PortfolioResult.NotFound();
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);

        if (!ApiVersion.HasEnoughNotice(sunsetDate, today))
        {
            var days = sunsetDate.DayNumber - today.DayNumber;

            // Somebody integrating against this needs a quarter to move off it. Naming both numbers
            // saves the person working out what would be acceptable (BR-API-03).
            return PortfolioResult.Refused(
                "SUNSET_TOO_SOON",
                "sunsetDate",
                $"That is {days} days away. A deprecation needs at least {ApiVersion.MinimumSunsetNoticeDays} days' notice.");
        }

        if (version.IsCurrent)
        {
            var replacement = await _dbContext.ApiVersions
                .AnyAsync(v => v.ApiCatalogEntryId == version.ApiCatalogEntryId
                    && v.Id != versionId && v.Status != ApiVersionStatus.Deprecated, cancellationToken)
                .ConfigureAwait(false);

            // Deprecating the only current version would leave the directory telling developers to
            // build against nothing (EX-142).
            return PortfolioResult.Conflict(
                "NO_REPLACEMENT",
                replacement
                    ? "Mark the replacement current first, then deprecate this one."
                    : "This is the only version. Publish a replacement before deprecating it.");
        }

        version.Status = ApiVersionStatus.Deprecated;
        version.SunsetDate = sunsetDate;
        version.ModifiedBy = actor;

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PortfolioResult.Done();
    }
}
