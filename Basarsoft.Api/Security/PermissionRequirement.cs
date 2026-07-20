using Microsoft.AspNetCore.Authorization;

namespace Basarsoft.Api.Security;

// Requirement for the per-resource admin policies: the caller must hold ONE SPECIFIC management
// permission (manage_users, manage_roles, ...), unlike AdminAccessRequirement's "any of them".
// One policy per manage_* permission is registered in Program.cs; PermissionHandler does the check.
public class PermissionRequirement : IAuthorizationRequirement
{
    // Policy names the admin controllers reference. Each maps 1:1 to a SeedData permission name.
    public const string ManageUsers = "ManageUsers";
    public const string ManageRoles = "ManageRoles";
    public const string ManagePermissions = "ManagePermissions";
    public const string ManagePois = "ManagePois";

    public PermissionRequirement(string permissionName) => PermissionName = permissionName;

    public string PermissionName { get; }
}
