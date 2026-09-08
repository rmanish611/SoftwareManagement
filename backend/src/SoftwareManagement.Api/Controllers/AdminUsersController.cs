using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftwareManagement.Application.Security;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// Back-office user administration (AZ-58 to AZ-60). Reading is open to the Auditor; every write is
/// Owner-only, and the last Owner is protected against removal by the service as well as by policy.
/// </summary>
[ApiController]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController(IAdminUserService users) : ControllerBase
{
    private readonly IAdminUserService _users = users;

    [HttpGet]
    [Authorize(Policy = Permissions.Administration.UserRead)]
    public async Task<ActionResult<IReadOnlyList<AdminUserSummary>>> List(CancellationToken cancellationToken) =>
        Ok(await _users.ListAsync(cancellationToken).ConfigureAwait(false));

    [HttpPost]
    [Authorize(Policy = Permissions.Administration.UserWrite)]
    public async Task<ActionResult<AdminUserSummary>> Create(CreateUserBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            var created = await _users
                .CreateAsync(new CreateUserRequest(body.Email, body.FullName, body.Role), ActorEmail(), cancellationToken)
                .ConfigureAwait(false);

            return created is null
                ? Problem(title: "User not created", statusCode: StatusCodes.Status422UnprocessableEntity)
                : CreatedAtAction(nameof(List), new { id = created.Id }, created);
        }
        catch (EmailAlreadyRegisteredException)
        {
            return Problem(
                title: "Email already registered",
                detail: "That email address already has an account.",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://softwaremanagement.example/errors/email-taken");
        }
        catch (ArgumentOutOfRangeException)
        {
            return Problem(
                title: "Unknown role",
                detail: "Role must be one of Owner, Sales, Editor or Auditor.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: "https://softwaremanagement.example/errors/unknown-role");
        }
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Permissions.Administration.UserWrite)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var done = await _users.DeactivateAsync(id, ActorEmail(), cancellationToken).ConfigureAwait(false);
            return done ? NoContent() : NotFound();
        }
        catch (LastOwnerException)
        {
            return Problem(
                title: "Last owner",
                detail: "The last remaining Owner cannot be deactivated. Promote someone else first.",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://softwaremanagement.example/errors/last-owner");
        }
    }

    [HttpPost("{id:guid}/role")]
    [Authorize(Policy = Permissions.Administration.RoleAssign)]
    public async Task<IActionResult> AssignRole(Guid id, AssignRoleBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            var done = await _users.AssignRoleAsync(id, body.Role, ActorEmail(), cancellationToken).ConfigureAwait(false);
            return done ? NoContent() : NotFound();
        }
        catch (LastOwnerException)
        {
            return Problem(
                title: "Last owner",
                detail: "The last remaining Owner cannot be moved to another role.",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://softwaremanagement.example/errors/last-owner");
        }
        catch (ArgumentOutOfRangeException)
        {
            return Problem(
                title: "Unknown role",
                detail: "Role must be one of Owner, Sales, Editor or Auditor.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: "https://softwaremanagement.example/errors/unknown-role");
        }
    }

    private string ActorEmail() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

/// <summary>Sign-in attempts, for the Auditor to investigate suspicious access (REQ-IAM-011).</summary>
[ApiController]
[Route("api/v1/admin/login-attempts")]
public sealed class LoginAttemptsController(IAdminUserService users) : ControllerBase
{
    private readonly IAdminUserService _users = users;

    [HttpGet]
    [Authorize(Policy = Permissions.Administration.AuditRead)]
    public async Task<ActionResult<IReadOnlyList<LoginAttemptSummary>>> List(
        [FromQuery] bool failuresOnly = false,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await _users.ListLoginAttemptsAsync(failuresOnly, take, cancellationToken).ConfigureAwait(false));
}

public sealed record CreateUserBody(string Email, string FullName, string Role);

public sealed record AssignRoleBody(string Role);
