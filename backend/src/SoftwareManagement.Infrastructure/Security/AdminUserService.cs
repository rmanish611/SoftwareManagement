using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Audit;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Identity;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Security;

/// <summary>
/// Back-office user administration. Every method here is Owner-only at the endpoint; the checks in
/// this class are the second line, enforced regardless of who calls it (BR-IAM-04).
/// </summary>
public sealed class AdminUserService(
    UserManager<AdminUser> userManager,
    RoleManager<AdminRole> roleManager,
    AppDbContext dbContext,
    IClock clock) : IAdminUserService
{
    private readonly UserManager<AdminUser> _userManager = userManager;
    private readonly RoleManager<AdminRole> _roleManager = roleManager;
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IClock _clock = clock;

    public async Task<IReadOnlyList<AdminUserSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var users = await _userManager.Users
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var summaries = new List<AdminUserSummary>(users.Count);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user).ConfigureAwait(false);
            summaries.Add(new AdminUserSummary(
                user.Id,
                user.Email ?? string.Empty,
                user.FullName,
                user.IsActive,
                [.. roles],
                user.LastLoginAtUtc));
        }

        return summaries;
    }

    public async Task<AdminUserSummary?> CreateAsync(CreateUserRequest request, string actorEmail, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email.Trim();

        if (await _userManager.FindByEmailAsync(email).ConfigureAwait(false) is not null)
        {
            throw new EmailAlreadyRegisteredException();
        }

        if (!RoleNames.All.Contains(request.Role, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Role, "Unknown role.");
        }

        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FullName = request.FullName.Trim(),
            // The account exists but cannot be used until the invitee sets a password: creating a
            // user never involves an administrator choosing someone else's password.
            IsActive = false,
            EmailConfirmed = false,
            CreatedBy = actorEmail,
        };

        var created = await _userManager.CreateAsync(user).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not create the user: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        await _userManager.AddToRoleAsync(user, request.Role).ConfigureAwait(false);

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = _clock.UtcNow,
            ActorEmail = actorEmail,
            Action = AuditActions.Created,
            EntityType = nameof(AdminUser),
            EntityId = user.Id,
            AfterJson = $"{{\"email\":\"{user.Email}\",\"role\":\"{request.Role}\"}}",
            CorrelationId = Guid.NewGuid(),
        });

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AdminUserSummary(user.Id, user.Email!, user.FullName, user.IsActive, [request.Role], null);
    }

    public async Task<bool> DeactivateAsync(Guid userId, string actorEmail, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        if (await WouldRemoveLastOwnerAsync(user).ConfigureAwait(false))
        {
            throw new LastOwnerException();
        }

        user.IsActive = false;
        await _userManager.UpdateAsync(user).ConfigureAwait(false);

        // Access ends now, not when the current access token happens to expire.
        var tokens = await _dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = _clock.UtcNow;
            token.RevokedReason = "user deactivated";
        }

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = _clock.UtcNow,
            ActorEmail = actorEmail,
            Action = AuditActions.UserDeactivated,
            EntityType = nameof(AdminUser),
            EntityId = user.Id,
            BeforeJson = "{\"isActive\":true}",
            AfterJson = "{\"isActive\":false}",
            CorrelationId = Guid.NewGuid(),
        });

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> AssignRoleAsync(Guid userId, string roleName, string actorEmail, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        if (!await _roleManager.RoleExistsAsync(roleName).ConfigureAwait(false))
        {
            throw new ArgumentOutOfRangeException(nameof(roleName), roleName, "Unknown role.");
        }

        var currentRoles = await _userManager.GetRolesAsync(user).ConfigureAwait(false);

        if (currentRoles.Contains(RoleNames.Owner, StringComparer.Ordinal)
            && roleName != RoleNames.Owner
            && await IsLastOwnerAsync(user).ConfigureAwait(false))
        {
            throw new LastOwnerException("The last remaining Owner cannot be moved to another role.");
        }

        await _userManager.RemoveFromRolesAsync(user, currentRoles).ConfigureAwait(false);
        await _userManager.AddToRoleAsync(user, roleName).ConfigureAwait(false);

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = _clock.UtcNow,
            ActorEmail = actorEmail,
            Action = AuditActions.RoleAssigned,
            EntityType = nameof(AdminUser),
            EntityId = user.Id,
            BeforeJson = $"{{\"roles\":\"{string.Join(',', currentRoles)}\"}}",
            AfterJson = $"{{\"roles\":\"{roleName}\"}}",
            CorrelationId = Guid.NewGuid(),
        });

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<LoginAttemptSummary>> ListLoginAttemptsAsync(bool failuresOnly, int take, CancellationToken cancellationToken)
    {
        var capped = Math.Clamp(take, 1, 100);

        var query = _dbContext.LoginAttempts.AsNoTracking();
        if (failuresOnly)
        {
            query = query.Where(a => !a.Succeeded);
        }

        return await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(capped)
            .Select(a => new LoginAttemptSummary(a.CreatedAtUtc, a.EmailAttempted, a.Succeeded, a.IpAddress, a.FailureReason))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> WouldRemoveLastOwnerAsync(AdminUser user) =>
        await _userManager.IsInRoleAsync(user, RoleNames.Owner).ConfigureAwait(false)
        && await IsLastOwnerAsync(user).ConfigureAwait(false);

    private async Task<bool> IsLastOwnerAsync(AdminUser user)
    {
        var owners = await _userManager.GetUsersInRoleAsync(RoleNames.Owner).ConfigureAwait(false);
        return owners.Count(o => o.IsActive || o.Id == user.Id) <= 1;
    }
}
