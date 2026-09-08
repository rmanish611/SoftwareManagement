using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using SoftwareManagement.Infrastructure.Security;

namespace SoftwareManagement.Api.Infrastructure;

/// <summary>
/// Turns `[Authorize(Policy = Permissions.Lead.Read)]` into a claim check without registering a
/// policy per permission by hand. There are 70 permissions in the catalogue and every one of them
/// needs a policy, so the provider builds them on demand from the policy name (BR-IAM-05).
/// </summary>
public sealed class PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        var existing = await base.GetPolicyAsync(policyName).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        // A policy name that looks like a permission (resource.action) becomes a requirement.
        if (!policyName.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
    }
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "Requires the permission {0}", Permission);
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        var granted = context.User.Claims.Any(c =>
            c.Type == TokenFactory.PermissionClaimType
            && string.Equals(c.Value, requirement.Permission, StringComparison.Ordinal));

        if (granted)
        {
            context.Succeed(requirement);
        }

        // Deliberately no context.Fail(): another handler may still grant it, and failing here
        // would short-circuit that. An unsatisfied requirement is already a denial.
        return Task.CompletedTask;
    }
}
