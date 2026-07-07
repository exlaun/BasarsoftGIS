namespace Basarsoft.Api.Data;

// Single source of truth for the seeded RBAC data and for what "admin access" means. Referenced by the
// startup seeder (to insert rows) and by the authorization check (to decide who may open /admin).
public static class SeedData
{
    public const string AdminRoleName = "Admin";
    public const string AdminRoleDescription = "Full access to the admin panel";

    // The user bootstrapped as an admin (granted the Admin role by the seeder), if present.
    public const string BootstrapAdminUsername = "demo0";

    // The shared permission catalogue: name (machine key) -> Turkish description. The mentor's examples
    // (point/line/polygon ekleme) plus the management permissions that gate the admin panel itself.
    public static readonly IReadOnlyList<(string Name, string Description)> Permissions = new[]
    {
        ("point_ekleme",   "Haritaya nokta ekleme"),
        ("line_ekleme",    "Haritaya çizgi ekleme"),
        ("polygon_ekleme", "Haritaya poligon ekleme"),
        ("user_yonetimi",  "Kullanıcı ekleme/çıkarma/güncelleme"),
        ("rol_yonetimi",   "Rol ekleme/çıkarma/silme"),
        ("yetki_yonetimi", "Yetki (permission) yönetimi"),
    };

    // Holding ANY of these effective permissions lets a user open the admin panel.
    public static readonly IReadOnlySet<string> AdminPermissions = new HashSet<string>
    {
        "user_yonetimi",
        "rol_yonetimi",
        "yetki_yonetimi",
    };
}
