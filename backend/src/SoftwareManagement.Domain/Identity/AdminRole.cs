using Microsoft.AspNetCore.Identity;

namespace SoftwareManagement.Domain.Identity;

/// <summary>
/// One of the four seeded roles (A-13). Roles exist to be mapped to permissions; no code ever
/// checks a role name, it checks a permission, so adding a fifth role later is a data change.
/// </summary>
public class AdminRole : IdentityRole<Guid>
{
    public AdminRole()
    {
    }

    public AdminRole(string roleName)
        : base(roleName)
    {
    }

    public string Description { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; } = [];
}

/// <summary>The four roles that exist. Names are stable: they are seeded and referenced by tests.</summary>
public static class RoleNames
{
    public const string Owner = "Owner";
    public const string Sales = "Sales";
    public const string Editor = "Editor";
    public const string Auditor = "Auditor";

    public static readonly IReadOnlyList<string> All = [Owner, Sales, Editor, Auditor];
}
