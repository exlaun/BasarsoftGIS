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
    public const string OperatorRoleDescription = "Can manage transportation routes and stops";
    public const string ViewerRoleName = "Viewer";
    public const string ViewerRoleDescription = "View-only access to the map and POIs";

    // Granted to the Operator role. Applied when the seeder first creates the role and, for any
    // brand-new name added here later, topped up onto an existing Operator role too (AdminSeeder step
    // 5b) — the same targeted, can't-undo-a-removal rule the Admin role gets. After that the admin
    // panel owns the set.
    public static readonly IReadOnlyList<string> OperatorPermissions = new[] { ManageTransportPermission };

    // The four management permission names, as constants so the per-resource authorization policies
    // (Program.cs / PermissionRequirement) and controllers reference the same spelling as the seed.
    public const string ManageUsersPermission = "manage_users";
    public const string ManageRolesPermission = "manage_roles";
    public const string ManagePermissionsPermission = "manage_permissions";
    public const string ManagePoisPermission = "manage_pois";
    public const string ManageTransportAdminPermission = "manage_transport_admin";

    // The transportation module's single permission. Deliberately NOT in AdminPermissions below: it
    // gates on-map operator actions (the Add-Stop tool + the Route panel's writes), not the admin
    // panel, so it needs no per-resource policy and holding it does not unlock /admin.
    public const string ManageTransportPermission = "manage_transport";

    // The shared permission catalogue: name (machine key) -> English description. Drawing permissions
    // plus the management permissions that gate the admin panel itself.
    public static readonly IReadOnlyList<(string Name, string Description)> Permissions = new[]
    {
        ("add_point",               "Create and manage your own point shapes"),
        ("add_line",                "Create and manage your own line shapes"),
        ("add_polygon",             "Create and manage your own polygon shapes"),
        (ManageUsersPermission,       "Create, update, and delete users"),
        (ManageRolesPermission,       "Create, update, and delete roles"),
        (ManagePermissionsPermission, "Manage permissions"),
        ("add_poi",                 "Add POIs to the map"),
        (ManagePoisPermission,        "Manage POIs and POI categories"),
        (ManageTransportPermission,   "Create and manage transportation routes and stops"),
        (ManageTransportAdminPermission, "Administer all transportation routes and stops"),
    };

    // Holding ANY of these effective permissions lets a user open the admin panel. Which sections
    // they can actually use inside it is decided per-resource (one policy per name; see Program.cs).
    public static readonly IReadOnlySet<string> AdminPermissions = new HashSet<string>
    {
        ManageUsersPermission,
        ManageRolesPermission,
        ManagePermissionsPermission,
        ManagePoisPermission,
        ManageTransportAdminPermission,
    };
}
