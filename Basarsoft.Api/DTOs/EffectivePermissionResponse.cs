namespace Basarsoft.Api.DTOs;

// One permission row for the user-permission editor. This is the payload that implements the mentor's
// rule: a permission that arrives via a role is not re-selectable and shows where it came from.
//   Source = "role"   -> inherited from a role (RoleName set); shown checked + disabled.
//   Source = "direct" -> granted straight to the user; toggleable.
//   Source = "none"   -> not granted; toggleable.
public class EffectivePermissionResponse
{
    public int PermissionId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Source { get; set; } = "none";

    // Set only when Source == "role": the name of a role that grants this permission.
    public string? RoleName { get; set; }

    // True when the permission comes from at least one of the user's roles.
    public bool IsInherited { get; set; }

    // True when the permission is granted directly to the user (independent of the role source).
    public bool IsDirect { get; set; }
}
