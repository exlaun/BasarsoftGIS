namespace Basarsoft.Api.Data;

// Single source of truth for the seeded RBAC data and for what "admin access" means. Referenced by the
// startup seeder (to insert rows) and by the authorization check (to decide who may open /admin).
public static class SeedData
{
    public const string AdminRoleName = "Admin";
    public const string AdminRoleDescription = "Full access to the admin panel";

    // The user bootstrapped as an admin (granted the Admin role by the seeder), if present.
    public const string BootstrapAdminUsername = "demo0";

    // The shared permission catalogue: name (machine key) -> English description. Drawing permissions
    // plus the management permissions that gate the admin panel itself.
    public static readonly IReadOnlyList<(string Name, string Description)> Permissions = new[]
    {
        ("add_point",          "Add points to the map"),
        ("add_line",           "Add lines to the map"),
        ("add_polygon",        "Add polygons to the map"),
        ("manage_users",       "Create, update, and delete users"),
        ("manage_roles",       "Create, update, and delete roles"),
        ("manage_permissions", "Manage permissions"),
    };

    // Holding ANY of these effective permissions lets a user open the admin panel.
    public static readonly IReadOnlySet<string> AdminPermissions = new HashSet<string>
    {
        "manage_users",
        "manage_roles",
        "manage_permissions",
    };
}
