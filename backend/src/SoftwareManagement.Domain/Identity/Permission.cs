using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Identity;

/// <summary>
/// One `resource:action` pair from `docs/blueprint/06-authz.md`. The permission catalogue is data,
/// checked by policy at every endpoint, so authorization can be reviewed as a table rather than by
/// reading controllers.
/// </summary>
public class Permission : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; } = [];
}

/// <summary>Join between a role and a permission.</summary>
public class RolePermission
{
    public Guid RoleId { get; set; }

    public AdminRole? Role { get; set; }

    public Guid PermissionId { get; set; }

    public Permission? Permission { get; set; }
}
