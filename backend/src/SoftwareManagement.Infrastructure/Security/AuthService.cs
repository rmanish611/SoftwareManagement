using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Audit;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Identity;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Security;

/// <summary>
/// Sign-in, token rotation and password management.
///
/// Two rules shape everything here. First, the caller learns nothing about whether an account
/// exists: every failure path costs a password-hash verification and returns the same shape
/// (BR-IAM-02). Second, refresh tokens rotate, and presenting one that was already rotated is
/// treated as theft rather than as a mistake (BR-IAM-03).
/// </summary>
public sealed partial class AuthService(
    UserManager<AdminUser> userManager,
    AppDbContext dbContext,
    TokenFactory tokenFactory,
    IOptions<LockoutOptions> lockoutOptions,
    IClock clock,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly UserManager<AdminUser> _userManager = userManager;
    private readonly AppDbContext _dbContext = dbContext;
    private readonly TokenFactory _tokenFactory = tokenFactory;
    private readonly LockoutOptions _lockout = lockoutOptions.Value;
    private readonly IClock _clock = clock;
    private readonly ILogger<AuthService> _logger = logger;

    public async Task<AuthOutcome> LoginAsync(LoginRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var email = request.Email.Trim();

        // The lockout is checked before the account is even looked up. Checking it afterwards
        // would leave an address that does not exist unthrottled, so an attacker could guess
        // addresses at full speed and learn which ones eventually start locking (BR-IAM-02).
        if (await IsLockedOutAsync(email, cancellationToken).ConfigureAwait(false))
        {
            await RecordAttemptAsync(email, false, context, LoginFailureReasons.LockedOut, cancellationToken).ConfigureAwait(false);
            return AuthOutcome.Fail(AuthFailure.LockedOut);
        }

        var user = await _userManager.FindByEmailAsync(email).ConfigureAwait(false);

        if (user is null)
        {
            // Verify a dummy hash so an unknown address costs the same time as a wrong password.
            _userManager.PasswordHasher.VerifyHashedPassword(new AdminUser(), DummyPasswordHash, request.Password);
            await RecordAttemptAsync(email, false, context, LoginFailureReasons.UnknownUser, cancellationToken).ConfigureAwait(false);
            return AuthOutcome.Fail(AuthFailure.InvalidCredentials);
        }

        if (!await _userManager.CheckPasswordAsync(user, request.Password).ConfigureAwait(false))
        {
            await RecordAttemptAsync(email, false, context, LoginFailureReasons.WrongPassword, cancellationToken).ConfigureAwait(false);
            return AuthOutcome.Fail(AuthFailure.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            await RecordAttemptAsync(email, false, context, LoginFailureReasons.Inactive, cancellationToken).ConfigureAwait(false);
            return AuthOutcome.Fail(AuthFailure.Inactive);
        }

        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
            {
                await RecordAttemptAsync(email, false, context, LoginFailureReasons.TwoFactorRequired, cancellationToken).ConfigureAwait(false);
                return AuthOutcome.Fail(AuthFailure.TwoFactorRequired);
            }

            var codeValid = await _userManager.VerifyTwoFactorTokenAsync(
                user,
                _userManager.Options.Tokens.AuthenticatorTokenProvider,
                request.TwoFactorCode).ConfigureAwait(false);

            if (!codeValid)
            {
                await RecordAttemptAsync(email, false, context, LoginFailureReasons.TwoFactorInvalid, cancellationToken).ConfigureAwait(false);
                return AuthOutcome.Fail(AuthFailure.TwoFactorInvalid);
            }
        }

        var result = await IssueTokensAsync(user, context, cancellationToken).ConfigureAwait(false);
        await RecordAttemptAsync(email, true, context, null, cancellationToken).ConfigureAwait(false);

        user.LastLoginAtUtc = _clock.UtcNow;
        _dbContext.AuditLogs.Add(NewAuditLog(AuditActions.SignedIn, user, context));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return AuthOutcome.Success(result);
    }

    public async Task<AuthOutcome> RefreshAsync(string refreshToken, RequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return AuthOutcome.Fail(AuthFailure.RefreshTokenInvalid);
        }

        var hash = TokenFactory.Hash(refreshToken);
        var stored = await _dbContext.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken).ConfigureAwait(false);

        if (stored?.User is null)
        {
            return AuthOutcome.Fail(AuthFailure.RefreshTokenInvalid);
        }

        var now = _clock.UtcNow;

        if (stored.WasRotated || stored.RevokedAtUtc is not null)
        {
            // A token that was already exchanged is being presented again: the only explanations are
            // theft or a compromised client, and both mean every token in the chain must die.
            await RevokeChainAsync(stored.UserId, "refresh token reuse detected", cancellationToken).ConfigureAwait(false);
            _dbContext.AuditLogs.Add(NewAuditLog(AuditActions.TokenChainRevoked, stored.User, context));
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            LogRefreshTokenReuse(_logger, stored.UserId);

            return AuthOutcome.Fail(AuthFailure.RefreshTokenReused);
        }

        if (!stored.IsActive(now) || !stored.User.IsActive)
        {
            return AuthOutcome.Fail(AuthFailure.RefreshTokenInvalid);
        }

        var result = await IssueTokensAsync(stored.User, context, cancellationToken).ConfigureAwait(false);

        stored.ReplacedByTokenHash = TokenFactory.Hash(result.RefreshToken);
        stored.RevokedAtUtc = now;
        stored.RevokedReason = "rotated";
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return AuthOutcome.Success(result);
    }

    public async Task LogoutAsync(string refreshToken, RequestContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = TokenFactory.Hash(refreshToken);
        var stored = await _dbContext.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken).ConfigureAwait(false);

        if (stored is null || stored.RevokedAtUtc is not null)
        {
            return;
        }

        stored.RevokedAtUtc = _clock.UtcNow;
        stored.RevokedReason = "sign out";

        if (stored.User is not null && context is not null)
        {
            _dbContext.AuditLogs.Add(NewAuditLog(AuditActions.SignedOut, stored.User, context));
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        var result = await _userManager
            .ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return false;
        }

        // A password change invalidates every other session: that is the point of changing it.
        await RevokeChainAsync(userId, "password changed", cancellationToken).ConfigureAwait(false);
        _dbContext.AuditLogs.Add(NewAuditLog(AuditActions.PasswordChanged, user, new RequestContext("internal", null)));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        var user = await _userManager.FindByEmailAsync(email.Trim()).ConfigureAwait(false);
        if (user is null)
        {
            // Deliberately silent: the endpoint answers 202 either way so it cannot enumerate users.
            return;
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);

        // P07 introduces the outbox and turns this into a queued email. Until then the token is
        // written to the audit trail so the owner can complete a reset without a mail server.
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = _clock.UtcNow,
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            Action = AuditActions.PasswordReset,
            EntityType = nameof(AdminUser),
            EntityId = user.Id,
            AfterJson = "{\"resetRequested\":true}",
            CorrelationId = Guid.NewGuid(),
        });

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogPasswordResetRequested(_logger, user.Id, token.Length);
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _userManager.FindByEmailAsync(request.Email.Trim()).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        var result = await _userManager
            .ResetPasswordAsync(user, request.Token, request.NewPassword).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return false;
        }

        await RevokeChainAsync(user.Id, "password reset", cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<LoginResult> IssueTokensAsync(AdminUser user, RequestContext context, CancellationToken cancellationToken)
    {
        var roles = await _userManager.GetRolesAsync(user).ConfigureAwait(false);
        var permissions = await PermissionsForRolesAsync(roles, cancellationToken).ConfigureAwait(false);

        var (accessToken, accessExpires) = _tokenFactory.CreateAccessToken(user, roles, permissions);
        var (refreshToken, refreshHash, refreshExpires) = _tokenFactory.CreateRefreshToken();

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAtUtc = refreshExpires,
            CreatedByIp = context.IpAddress,
            CreatedBy = user.Email ?? "system",
        });

        return new LoginResult(
            accessToken,
            accessExpires,
            refreshToken,
            refreshExpires,
            user.FullName,
            user.Email ?? string.Empty,
            [.. roles],
            permissions);
    }

    private async Task<IReadOnlyList<string>> PermissionsForRolesAsync(IEnumerable<string> roles, CancellationToken cancellationToken)
    {
        var roleNames = roles.ToArray();
        if (roleNames.Length == 0)
        {
            return [];
        }

        return await _dbContext.RolePermissions
            .Where(rp => rp.Role != null && roleNames.Contains(rp.Role.Name!))
            .Select(rp => rp.Permission!.Name)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsLockedOutAsync(string email, CancellationToken cancellationToken)
    {
        var windowStart = _clock.UtcNow.AddMinutes(-_lockout.WindowMinutes);

        var recentFailures = await _dbContext.LoginAttempts
            .Where(a => a.EmailAttempted == email && !a.Succeeded && a.CreatedAtUtc >= windowStart)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        if (recentFailures < _lockout.MaxFailedAttempts)
        {
            return false;
        }

        // The lock lasts LockoutMinutes from the most recent failure, so it clears on its own.
        var lastFailure = await _dbContext.LoginAttempts
            .Where(a => a.EmailAttempted == email && !a.Succeeded)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return lastFailure.AddMinutes(_lockout.LockoutMinutes) > _clock.UtcNow;
    }

    private async Task RecordAttemptAsync(string email, bool succeeded, RequestContext context, string? reason, CancellationToken cancellationToken)
    {
        _dbContext.LoginAttempts.Add(new LoginAttempt
        {
            Id = Guid.NewGuid(),
            EmailAttempted = email,
            Succeeded = succeeded,
            IpAddress = context.IpAddress,
            UserAgent = context.UserAgent,
            FailureReason = reason,
            CreatedBy = "anonymous",
        });

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RevokeChainAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var active = await _dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var token in active)
        {
            token.RevokedAtUtc = now;
            token.RevokedReason = reason;
        }
    }

    private AuditLog NewAuditLog(string action, AdminUser user, RequestContext context) => new()
    {
        Id = Guid.NewGuid(),
        OccurredAtUtc = _clock.UtcNow,
        ActorUserId = user.Id,
        ActorEmail = user.Email,
        ActorIp = context.IpAddress,
        Action = action,
        EntityType = nameof(AdminUser),
        EntityId = user.Id,
        CorrelationId = Guid.NewGuid(),
    };

    // Source-generated logging: the arguments are only formatted when the level is enabled.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reuse detected for user {userId}; the whole chain was revoked.")]
    private static partial void LogRefreshTokenReuse(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password reset requested for {userId}. Token length {length}.")]
    private static partial void LogPasswordResetRequested(ILogger logger, Guid userId, int length);

    /// <summary>
    /// A real Identity v3 hash of a value nobody knows. Verifying against it makes the unknown-user
    /// path cost the same as the wrong-password path, so response timing does not reveal which
    /// addresses are registered.
    /// </summary>
    private const string DummyPasswordHash =
        "AQAAAAIAAYagAAAAEL9wLbGZ0Yl0iJZpEwEo5oWvVQxFqz1p3Ff5rMlUE0AqvJH5g3rGZ8kQpWm0nJk3Rw==";
}
