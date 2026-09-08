using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftwareManagement.Application.Security;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// Sign-in, token rotation and password management.
///
/// Every failure that could reveal whether an account exists returns the same body and the same
/// status, and the refresh token travels in an HttpOnly, SameSite=Strict cookie so a script on the
/// page cannot read it (BR-IAM-02, BR-IAM-03).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    private const string RefreshCookieName = "sm_refresh";

    private readonly IAuthService _authService = authService;

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var outcome = await _authService
            .LoginAsync(new LoginRequest(body.Email, body.Password, body.TwoFactorCode), CurrentContext(), cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Succeeded)
        {
            SetRefreshCookie(outcome.Result!.RefreshToken, outcome.Result.RefreshTokenExpiresAtUtc);
            return Ok(LoginResponse.From(outcome.Result));
        }

        return outcome.Failure switch
        {
            AuthFailure.LockedOut => Problem(
                title: "Account locked",
                detail: "Too many failed sign-in attempts. Try again in a few minutes.",
                statusCode: StatusCodes.Status423Locked,
                type: "https://softwaremanagement.example/errors/account-locked"),

            AuthFailure.TwoFactorRequired => Problem(
                title: "Two-factor code required",
                detail: "Enter the six-digit code from your authenticator app.",
                statusCode: StatusCodes.Status401Unauthorized,
                type: "https://softwaremanagement.example/errors/mfa-required"),

            // Inactive, unknown address, wrong password and an invalid code are deliberately
            // indistinguishable to the caller.
            _ => Problem(
                title: "Sign-in failed",
                detail: "Email or password is incorrect.",
                statusCode: StatusCodes.Status401Unauthorized,
                type: "https://softwaremanagement.example/errors/invalid-credentials"),
        };
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh(CancellationToken cancellationToken)
    {
        var token = Request.Cookies[RefreshCookieName] ?? Request.Headers["X-Refresh-Token"].FirstOrDefault();

        var outcome = await _authService.RefreshAsync(token ?? string.Empty, CurrentContext(), cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Succeeded)
        {
            SetRefreshCookie(outcome.Result!.RefreshToken, outcome.Result.RefreshTokenExpiresAtUtc);
            return Ok(LoginResponse.From(outcome.Result));
        }

        Response.Cookies.Delete(RefreshCookieName);

        return Problem(
            title: "Session expired",
            detail: "Sign in again.",
            statusCode: StatusCodes.Status401Unauthorized,
            type: outcome.Failure == AuthFailure.RefreshTokenReused
                ? "https://softwaremanagement.example/errors/token-reuse"
                : "https://softwaremanagement.example/errors/session-expired");
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var token = Request.Cookies[RefreshCookieName] ?? Request.Headers["X-Refresh-Token"].FirstOrDefault();
        await _authService.LogoutAsync(token ?? string.Empty, CurrentContext(), cancellationToken).ConfigureAwait(false);
        Response.Cookies.Delete(RefreshCookieName);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var changed = await _authService
            .ChangePasswordAsync(userId.Value, new ChangePasswordRequest(body.CurrentPassword, body.NewPassword), cancellationToken)
            .ConfigureAwait(false);

        if (!changed)
        {
            return Problem(
                title: "Password not changed",
                detail: "The current password is wrong, or the new password does not meet the policy: " +
                        "at least 12 characters using three of uppercase, lowercase, digits and symbols.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: "https://softwaremanagement.example/errors/password-policy");
        }

        Response.Cookies.Delete(RefreshCookieName);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await _authService.RequestPasswordResetAsync(body.Email, cancellationToken).ConfigureAwait(false);

        // Always 202, registered or not, so the endpoint cannot enumerate accounts.
        return Accepted();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var reset = await _authService
            .ResetPasswordAsync(new ResetPasswordRequest(body.Email, body.Token, body.NewPassword), cancellationToken)
            .ConfigureAwait(false);

        if (reset)
        {
            return NoContent();
        }

        return Problem(
            title: "Reset link is no longer valid",
            detail: "Request a new password reset link.",
            statusCode: StatusCodes.Status410Gone,
            type: "https://softwaremanagement.example/errors/reset-token-consumed");
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<CurrentUserResponse> Me() =>
        Ok(new CurrentUserResponse(
            User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            [.. User.FindAll(ClaimTypes.Role).Select(c => c.Value)],
            [.. User.FindAll("permission").Select(c => c.Value)]));

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
            ? id
            : null;

    private RequestContext CurrentContext() => new(
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        Request.Headers.UserAgent.ToString());

    private void SetRefreshCookie(string token, DateTime expiresAtUtc) =>
        Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero),
            Path = "/api/v1/auth",
        });
}

public sealed record LoginBody(string Email, string Password, string? TwoFactorCode);

public sealed record ChangePasswordBody(string CurrentPassword, string NewPassword);

public sealed record ForgotPasswordBody(string Email);

public sealed record ResetPasswordBody(string Email, string Token, string NewPassword);

public sealed record CurrentUserResponse(
    string FullName,
    string Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

/// <summary>
/// The sign-in response. The refresh token is not in the body: it is set as an HttpOnly cookie so
/// no script on the page can read it.
/// </summary>
public sealed record LoginResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string FullName,
    string Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions)
{
    public static LoginResponse From(LoginResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new LoginResponse(
            result.AccessToken,
            result.AccessTokenExpiresAtUtc,
            result.FullName,
            result.Email,
            result.Roles,
            result.Permissions);
    }
}
