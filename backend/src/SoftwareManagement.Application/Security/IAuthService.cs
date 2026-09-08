namespace SoftwareManagement.Application.Security;

/// <summary>
/// Every authentication path the back office has. Implemented in Infrastructure because it needs
/// the identity stores; declared here so the API layer depends on the contract, not on Identity.
/// </summary>
public interface IAuthService
{
    Task<AuthOutcome> LoginAsync(LoginRequest request, RequestContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Rotates a refresh token. Presenting one that was already rotated means it leaked: the whole
    /// chain is revoked and an alert is raised (BR-IAM-03).
    /// </summary>
    Task<AuthOutcome> RefreshAsync(string refreshToken, RequestContext context, CancellationToken cancellationToken);

    Task LogoutAsync(string refreshToken, RequestContext context, CancellationToken cancellationToken);

    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Always reports success to the caller, whether or not the address is registered, so the
    /// endpoint cannot be used to discover who has an account.
    /// </summary>
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken);

    Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
}

/// <summary>Back-office user administration. Owner-only at the endpoint (AZ-58, AZ-59).</summary>
public interface IAdminUserService
{
    Task<IReadOnlyList<AdminUserSummary>> ListAsync(CancellationToken cancellationToken);

    Task<AdminUserSummary?> CreateAsync(CreateUserRequest request, string actorEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Returns false when the user does not exist, and throws <see cref="LastOwnerException"/> when
    /// the request would leave the system with no Owner (BR-IAM-04).
    /// </summary>
    Task<bool> DeactivateAsync(Guid userId, string actorEmail, CancellationToken cancellationToken);

    Task<bool> AssignRoleAsync(Guid userId, string roleName, string actorEmail, CancellationToken cancellationToken);

    Task<IReadOnlyList<LoginAttemptSummary>> ListLoginAttemptsAsync(bool failuresOnly, int take, CancellationToken cancellationToken);
}

/// <summary>Raised when an operation would remove the last Owner from the system.</summary>
public sealed class LastOwnerException : InvalidOperationException
{
    public LastOwnerException()
        : base("The last remaining Owner cannot be deactivated or demoted.")
    {
    }

    public LastOwnerException(string message)
        : base(message)
    {
    }

    public LastOwnerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when an email address is already registered.</summary>
public sealed class EmailAlreadyRegisteredException : InvalidOperationException
{
    public EmailAlreadyRegisteredException()
        : base("That email address already has an account.")
    {
    }

    public EmailAlreadyRegisteredException(string message)
        : base(message)
    {
    }

    public EmailAlreadyRegisteredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
