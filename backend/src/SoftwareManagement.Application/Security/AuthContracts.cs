namespace SoftwareManagement.Application.Security;

/// <summary>What the caller sends to sign in. The two-factor code is only used once enrolled.</summary>
public sealed record LoginRequest(string Email, string Password, string? TwoFactorCode);

/// <summary>
/// What a successful sign-in returns. The refresh token is delivered separately as an HttpOnly
/// cookie; it is returned here only so tests and non-browser clients can drive the flow.
/// </summary>
public sealed record LoginResult(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    string FullName,
    string Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

/// <summary>
/// Why a sign-in failed. The API turns every one of these into a response that reveals nothing
/// about whether the account exists (BR-IAM-02); the distinction exists for the audit trail.
/// </summary>
public enum AuthFailure
{
    None = 0,
    InvalidCredentials = 1,
    LockedOut = 2,
    Inactive = 3,
    TwoFactorRequired = 4,
    TwoFactorInvalid = 5,
    RefreshTokenInvalid = 6,
    RefreshTokenReused = 7,
}

public sealed record AuthOutcome(AuthFailure Failure, LoginResult? Result)
{
    public bool Succeeded => Failure == AuthFailure.None && Result is not null;

    public static AuthOutcome Success(LoginResult result) => new(AuthFailure.None, result);

    public static AuthOutcome Fail(AuthFailure failure) => new(failure, null);
}

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record CreateUserRequest(string Email, string FullName, string Role);

public sealed record AdminUserSummary(
    Guid Id,
    string Email,
    string FullName,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTime? LastLoginAtUtc);

public sealed record LoginAttemptSummary(
    DateTime OccurredAtUtc,
    string EmailAttempted,
    bool Succeeded,
    string IpAddress,
    string? FailureReason);

/// <summary>
/// The context an authentication call needs about its caller. Passed in rather than read from a
/// static so the service stays testable without an HTTP request.
/// </summary>
public sealed record RequestContext(string IpAddress, string? UserAgent);
