namespace Basarsoft.Api.DTOs;

// A role plus the ids of the permissions it grants (drives the role-permission editor's checkboxes).
public class RoleResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public IReadOnlyList<int> PermissionIds { get; set; } = Array.Empty<int>();
}

// Lightweight role reference (id + name) embedded in AdminUserResponse so the Users table can list
// which roles each user holds without shipping every role's permissions.
public class RoleSummary
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
