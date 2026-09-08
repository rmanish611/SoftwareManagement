namespace SoftwareManagement.Infrastructure.Security;

/// <summary>
/// Token lifetimes from BR-IAM-03 and NFR-SEC-02. They are configuration rather than constants so
/// a deployment can shorten them, but the defaults are the approved numbers and the tests assert
/// against those numbers, so lengthening them silently would fail the build.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;

    public string Issuer { get; set; } = "software-management";

    public string Audience { get; set; } = "software-management-admin";

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 14;
}

/// <summary>Lockout policy from BR-IAM-02.</summary>
public sealed class LockoutOptions
{
    public const string SectionName = "Lockout";

    public int MaxFailedAttempts { get; set; } = 5;

    public int WindowMinutes { get; set; } = 15;

    public int LockoutMinutes { get; set; } = 15;
}
