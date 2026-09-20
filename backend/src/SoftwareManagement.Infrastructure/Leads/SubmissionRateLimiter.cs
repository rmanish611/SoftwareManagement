using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Leads;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Leads;

/// <summary>
/// How many submissions one address is allowed (BR-LEAD-04).
///
/// The count comes from the submissions table rather than from memory, for two reasons: a restart
/// must not hand a flood a fresh allowance, and the evidence of what happened has to survive in a
/// place the owner can look at afterwards. Spam rows count towards the limit, because a bot that
/// trips the honeypot on every attempt is exactly what the limit is for.
/// </summary>
public sealed class SubmissionRateLimiter(AppDbContext dbContext, IClock clock) : ISubmissionRateLimiter
{
    public const int PerShortWindow = 5;
    public const int ShortWindowMinutes = 10;
    public const int PerDay = 20;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly IClock _clock = clock;

    public async Task<RateLimitDecision> CheckAsync(string ipAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return RateLimitDecision.Allow();
        }

        var now = _clock.UtcNow;
        var shortWindowStart = now.AddMinutes(-ShortWindowMinutes);
        var dayStart = now.AddHours(-24);

        // One query for both windows: the day's rows are a superset of the ten minutes' rows, so
        // there is nothing to gain from asking twice.
        var recent = await _dbContext.FormSubmissions
            .AsNoTracking()
            .Where(s => s.IpAddress == ipAddress && s.CreatedAtUtc >= dayStart)
            .Select(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var inShortWindow = recent.Count(t => t >= shortWindowStart);

        if (inShortWindow >= PerShortWindow)
        {
            // Wait until the oldest submission in the window falls out of it, which is the earliest
            // moment another one could be allowed. A fixed number would either be a lie or overly
            // punitive.
            var oldestInWindow = recent.Where(t => t >= shortWindowStart).Min();
            var waitUntil = oldestInWindow.AddMinutes(ShortWindowMinutes);
            return RateLimitDecision.Deny(SecondsUntil(now, waitUntil));
        }

        if (recent.Count >= PerDay)
        {
            var waitUntil = recent.Min().AddHours(24);
            return RateLimitDecision.Deny(SecondsUntil(now, waitUntil));
        }

        return RateLimitDecision.Allow();
    }

    private static int SecondsUntil(DateTime now, DateTime target)
    {
        var seconds = (int)Math.Ceiling((target - now).TotalSeconds);

        // Never zero: a Retry-After of 0 invites an immediate retry that would be refused again.
        return Math.Max(1, seconds);
    }
}
