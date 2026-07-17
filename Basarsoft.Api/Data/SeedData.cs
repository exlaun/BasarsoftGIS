namespace Basarsoft.Api.Data;

// Single source of truth for the seeded RBAC data and for what "admin access" means. Referenced by the
// startup seeder (to insert rows) and by the authorization check (to decide who may open /admin).
public static class SeedData
{
    public const string AdminRoleName = "Admin";
    public const string AdminRoleDescription = "Full access to the admin panel";

    // The user bootstrapped as an admin (granted the Admin role by the seeder), if present. This is
    // also the demo dataset's admin account, so a normal startup after a demo seed finds the grant
    // already in place and no-ops.
    public const string BootstrapAdminUsername = "admin";

    // POI module roles. Nothing in the code resolves roles by these names except the seeder itself.
    public const string OperatorRoleName = "Operator";
    public const string OperatorRoleDescription = "Can add POIs on the map";
    public const string ViewerRoleName = "Viewer";
    public const string ViewerRoleDescription = "View-only access to the map and POIs";

    // Granted to the Operator role when the seeder first creates it. After that, the admin panel owns
    // the role's permission set (same hands-off rule as the Admin role).
    public static readonly IReadOnlyList<string> OperatorPermissions = new[] { "add_poi" };

    // The shared permission catalogue: name (machine key) -> English description. Drawing permissions
    // plus the management permissions that gate the admin panel itself.
    public static readonly IReadOnlyList<(string Name, string Description)> Permissions = new[]
    {
        ("add_point",          "Create and manage your own point shapes"),
        ("add_line",           "Create and manage your own line shapes"),
        ("add_polygon",        "Create and manage your own polygon shapes"),
        ("manage_users",       "Create, update, and delete users"),
        ("manage_roles",       "Create, update, and delete roles"),
        ("manage_permissions", "Manage permissions"),
        ("add_poi",            "Add POIs to the map"),
        ("manage_pois",        "Manage POIs and POI categories"),
    };

    // Holding ANY of these effective permissions lets a user open the admin panel.
    public static readonly IReadOnlySet<string> AdminPermissions = new HashSet<string>
    {
        "manage_users",
        "manage_roles",
        "manage_permissions",
        "manage_pois",
    };
}
