using System.Globalization;

namespace Basarsoft.Api.Data;

// The deterministic "Turkey Explorer" demo manifest. DemoSeeder combines this plain data with the
// 81 province geometries: every province produces one admin marker and one public-institution POI
// whose kind rotates through ProvincePoiTemplates so the nationwide layer reads realistically.
public static class DemoData
{
    public const string Password = "secret123";

    public const int ExpectedUserCount = 17;
    public const int ExpectedShapeCount = 164;
    public const int ExpectedPoiCount = 139;
    public const int ExpectedCategoryCount = 42;
    public const int ExpectedAreaCount = 15;
    public const int ExpectedProvinceCount = 81;

    public const string RegionalManagerRoleName = "Regional Manager";
    public const string EditorRoleName = "Editor";

    public sealed record DemoUser(
        string Username,
        string Role,
        string[] DirectPermissions,
        string? AreaWkt,
        string Demonstrates);

    // Order is intentional: after seed-demo restarts seq_users, these rows receive ids 1..17.
    public static readonly IReadOnlyList<DemoUser> Users =
    [
        new("admin", SeedData.AdminRoleName, [], null,
            "Full permissions and a private 100-shape nationwide inventory"),

        new("marmara_manager", RegionalManagerRoleName, [], null,
            "Marmara boundary inherited from the Regional Manager role"),
        new("aegean_manager", RegionalManagerRoleName, [], AegeanRegionWkt,
            "Aegean user area overrides the role's Marmara default"),
        new("mediterranean_manager", RegionalManagerRoleName, [], MediterraneanRegionWkt,
            "Mediterranean user-area override"),
        new("central_manager", RegionalManagerRoleName, [], CentralAnatoliaRegionWkt,
            "Central Anatolia user-area override"),
        new("blacksea_manager", RegionalManagerRoleName, [], BlackSeaRegionWkt,
            "Black Sea user-area override"),
        new("eastern_manager", RegionalManagerRoleName, [], EasternAnatoliaRegionWkt,
            "Eastern Anatolia user-area override"),
        new("southeast_manager", RegionalManagerRoleName, [], SoutheastRegionWkt,
            "Southeastern Anatolia user-area override"),

        new("ankara_editor", EditorRoleName, ["add_polygon"], AnkaraAreaWkt,
            "Role point/line permissions plus a direct polygon permission"),
        new("istanbul_editor", EditorRoleName, [], IstanbulAreaWkt,
            "Point and line editing inside Istanbul"),
        new("izmir_editor", EditorRoleName, [], IzmirAreaWkt,
            "Point and line editing inside Izmir"),
        new("antalya_editor", EditorRoleName, [], AntalyaAreaWkt,
            "Point and line editing inside Antalya"),

        new("istanbul_operator", SeedData.OperatorRoleName, [], IstanbulAreaWkt,
            "Role-inherited POI creation inside Istanbul"),
        new("antalya_operator", SeedData.OperatorRoleName, [], AntalyaAreaWkt,
            "Role-inherited POI creation inside Antalya"),
        new("gaziantep_operator", SeedData.OperatorRoleName, [], GaziantepAreaWkt,
            "Role-inherited POI creation inside Gaziantep"),
        new("trabzon_operator", SeedData.OperatorRoleName, [], TrabzonAreaWkt,
            "Role-inherited POI creation inside Trabzon"),

        new("viewer", SeedData.ViewerRoleName, [], null,
            "True read-only access, including three immutable legacy drawings"),
    ];

    public sealed record DemoRole(string Name, string Description, string[] Permissions, string? AreaWkt);

    public static readonly IReadOnlyList<DemoRole> Roles =
    [
        new(RegionalManagerRoleName, "Creates and manages a private inventory within an assigned region",
            ["add_point", "add_line", "add_polygon"], MarmaraRegionWkt),
        new(EditorRoleName, "Creates and manages private points and routes",
            ["add_point", "add_line"], null),
    ];

    // Geographic authorization uses simple valid polygons. The demo contract validates every
    // restricted shape and featured POI with Covers() before committing.
    private const string MarmaraRegionWkt =
        "POLYGON((26.00 39.40, 30.80 39.40, 31.60 40.20, 31.30 42.10, 25.70 42.10, 26.00 39.40))";
    private const string AegeanRegionWkt =
        "POLYGON((25.70 36.50, 30.20 36.50, 30.50 40.10, 25.70 40.10, 25.70 36.50))";
    private const string MediterraneanRegionWkt =
        "POLYGON((28.00 35.70, 36.80 35.70, 37.80 38.20, 30.00 38.40, 28.00 35.70))";
    private const string CentralAnatoliaRegionWkt =
        "POLYGON((29.20 37.20, 37.60 37.20, 38.00 40.80, 30.00 41.20, 29.20 37.20))";
    private const string BlackSeaRegionWkt =
        "POLYGON((29.00 39.90, 41.80 39.90, 42.20 42.30, 29.00 42.30, 29.00 39.90))";
    private const string EasternAnatoliaRegionWkt =
        "POLYGON((36.60 37.00, 45.10 37.00, 45.10 41.50, 36.60 41.50, 36.60 37.00))";
    private const string SoutheastRegionWkt =
        "POLYGON((35.00 35.80, 42.80 35.80, 42.80 38.60, 35.00 38.60, 35.00 35.80))";

    private const string AnkaraAreaWkt =
        "POLYGON((31.80 39.20, 33.80 39.20, 33.80 40.60, 31.80 40.60, 31.80 39.20))";
    private const string IstanbulAreaWkt =
        "POLYGON((28.00 40.70, 29.90 40.70, 29.90 41.60, 28.00 41.60, 28.00 40.70))";
    private const string IzmirAreaWkt =
        "POLYGON((26.70 37.70, 28.30 37.70, 28.30 39.40, 26.70 39.40, 26.70 37.70))";
    private const string AntalyaAreaWkt =
        "POLYGON((29.10 36.00, 32.20 36.00, 32.20 37.70, 29.10 37.70, 29.10 36.00))";
    private const string GaziantepAreaWkt =
        "POLYGON((36.30 36.20, 38.30 36.20, 38.30 37.80, 36.30 37.80, 36.30 36.20))";
    private const string TrabzonAreaWkt =
        "POLYGON((38.50 40.40, 40.80 40.40, 40.80 41.60, 38.50 41.60, 38.50 40.40))";

    private const string ColorRed = "#dc2626";
    private const string ColorOrange = "#f97316";
    private const string ColorAmber = "#f59e0b";
    private const string ColorGreen = "#16a34a";
    private const string ColorBlue = "#2563eb";
    private const string ColorCyan = "#0891b2";
    private const string ColorIndigo = "#4f46e5";
    private const string ColorViolet = "#7c3aed";

    // Exact province set expected from provinces.geojson. Region and color power the nationwide
    // admin markers and make all seven regions visually distinguishable.
    public sealed record DemoProvince(string Name, string Region, string Color);

    public static readonly IReadOnlyList<DemoProvince> Provinces =
    [
        // Marmara (11)
        new("Balıkesir", "Marmara", ColorBlue), new("Bilecik", "Marmara", ColorBlue),
        new("Bursa", "Marmara", ColorBlue), new("Çanakkale", "Marmara", ColorBlue),
        new("Edirne", "Marmara", ColorBlue), new("İstanbul", "Marmara", ColorBlue),
        new("Kırklareli", "Marmara", ColorBlue), new("Kocaeli", "Marmara", ColorBlue),
        new("Sakarya", "Marmara", ColorBlue), new("Tekirdağ", "Marmara", ColorBlue),
        new("Yalova", "Marmara", ColorBlue),

        // Aegean (8)
        new("Afyonkarahisar", "Aegean", ColorOrange), new("Aydın", "Aegean", ColorOrange),
        new("Denizli", "Aegean", ColorOrange), new("İzmir", "Aegean", ColorOrange),
        new("Kütahya", "Aegean", ColorOrange), new("Manisa", "Aegean", ColorOrange),
        new("Muğla", "Aegean", ColorOrange), new("Uşak", "Aegean", ColorOrange),

        // Mediterranean (8)
        new("Adana", "Mediterranean", ColorAmber), new("Antalya", "Mediterranean", ColorAmber),
        new("Burdur", "Mediterranean", ColorAmber), new("Hatay", "Mediterranean", ColorAmber),
        new("Isparta", "Mediterranean", ColorAmber), new("Kahramanmaraş", "Mediterranean", ColorAmber),
        new("Mersin", "Mediterranean", ColorAmber), new("Osmaniye", "Mediterranean", ColorAmber),

        // Central Anatolia (13)
        new("Aksaray", "Central Anatolia", ColorGreen), new("Ankara", "Central Anatolia", ColorGreen),
        new("Çankırı", "Central Anatolia", ColorGreen), new("Eskişehir", "Central Anatolia", ColorGreen),
        new("Karaman", "Central Anatolia", ColorGreen), new("Kayseri", "Central Anatolia", ColorGreen),
        new("Kırıkkale", "Central Anatolia", ColorGreen), new("Kırşehir", "Central Anatolia", ColorGreen),
        new("Konya", "Central Anatolia", ColorGreen), new("Nevşehir", "Central Anatolia", ColorGreen),
        new("Niğde", "Central Anatolia", ColorGreen), new("Sivas", "Central Anatolia", ColorGreen),
        new("Yozgat", "Central Anatolia", ColorGreen),

        // Black Sea (18)
        new("Amasya", "Black Sea", ColorCyan), new("Artvin", "Black Sea", ColorCyan),
        new("Bartın", "Black Sea", ColorCyan), new("Bayburt", "Black Sea", ColorCyan),
        new("Bolu", "Black Sea", ColorCyan), new("Çorum", "Black Sea", ColorCyan),
        new("Düzce", "Black Sea", ColorCyan), new("Giresun", "Black Sea", ColorCyan),
        new("Gümüşhane", "Black Sea", ColorCyan), new("Karabük", "Black Sea", ColorCyan),
        new("Kastamonu", "Black Sea", ColorCyan), new("Ordu", "Black Sea", ColorCyan),
        new("Rize", "Black Sea", ColorCyan), new("Samsun", "Black Sea", ColorCyan),
        new("Sinop", "Black Sea", ColorCyan), new("Tokat", "Black Sea", ColorCyan),
        new("Trabzon", "Black Sea", ColorCyan), new("Zonguldak", "Black Sea", ColorCyan),

        // Eastern Anatolia (14)
        new("Ağrı", "Eastern Anatolia", ColorViolet), new("Ardahan", "Eastern Anatolia", ColorViolet),
        new("Bingöl", "Eastern Anatolia", ColorViolet), new("Bitlis", "Eastern Anatolia", ColorViolet),
        new("Elazığ", "Eastern Anatolia", ColorViolet), new("Erzincan", "Eastern Anatolia", ColorViolet),
        new("Erzurum", "Eastern Anatolia", ColorViolet), new("Hakkari", "Eastern Anatolia", ColorViolet),
        new("Iğdır", "Eastern Anatolia", ColorViolet), new("Kars", "Eastern Anatolia", ColorViolet),
        new("Malatya", "Eastern Anatolia", ColorViolet), new("Muş", "Eastern Anatolia", ColorViolet),
        new("Tunceli", "Eastern Anatolia", ColorViolet), new("Van", "Eastern Anatolia", ColorViolet),

        // Southeastern Anatolia (9)
        new("Adıyaman", "Southeastern Anatolia", ColorRed),
        new("Batman", "Southeastern Anatolia", ColorRed),
        new("Diyarbakır", "Southeastern Anatolia", ColorRed),
        new("Gaziantep", "Southeastern Anatolia", ColorRed),
        new("Kilis", "Southeastern Anatolia", ColorRed),
        new("Mardin", "Southeastern Anatolia", ColorRed),
        new("Siirt", "Southeastern Anatolia", ColorRed),
        new("Şanlıurfa", "Southeastern Anatolia", ColorRed),
        new("Şırnak", "Southeastern Anatolia", ColorRed),
    ];

    public sealed record DemoShape(
        string Owner,
        string Type,
        string Name,
        string Color,
        string Wkt,
        int DaysAgo);

    // These 83 explicit shapes combine with 81 generated admin province markers to total 164.
    public static readonly IReadOnlyList<DemoShape> Shapes = BuildShapes();

    private sealed record ManagerSeed(string Owner, string Label, double Lon, double Lat, string Color);

    private static IReadOnlyList<DemoShape> BuildShapes()
    {
        var rows = new List<DemoShape>
        {
            // Admin: ten real cross-country routes.
            new("admin", "line", "İstanbul–Ankara Route", ColorGreen,
                "LINESTRING(28.98 41.01, 30.40 40.77, 31.60 40.30, 32.86 39.93)", 90),
            new("admin", "line", "Ankara–İzmir Route", ColorGreen,
                "LINESTRING(32.86 39.93, 31.10 39.20, 29.40 38.80, 27.14 38.42)", 88),
            new("admin", "line", "İstanbul–Bursa–İzmir Route", ColorGreen,
                "LINESTRING(29.00 41.02, 29.06 40.20, 27.14 38.42)", 86),
            new("admin", "line", "Ankara–Konya–Antalya Route", ColorGreen,
                "LINESTRING(32.86 39.93, 32.49 37.87, 30.71 36.90)", 84),
            new("admin", "line", "Adana–Gaziantep–Şanlıurfa Route", ColorGreen,
                "LINESTRING(35.32 37.00, 37.38 37.07, 38.79 37.17)", 82),
            new("admin", "line", "Samsun–Trabzon Coastal Route", ColorCyan,
                "LINESTRING(36.33 41.29, 37.88 40.98, 39.72 41.00)", 80),
            new("admin", "line", "Erzurum–Kars Route", ColorViolet,
                "LINESTRING(41.27 39.90, 42.86 40.60)", 78),
            new("admin", "line", "Diyarbakır–Van Route", ColorViolet,
                "LINESTRING(40.23 37.91, 42.10 38.10, 43.38 38.50)", 76),
            new("admin", "line", "İzmir–Muğla–Antalya Coastal Route", ColorOrange,
                "LINESTRING(27.14 38.42, 28.36 37.22, 30.71 36.90)", 74),
            new("admin", "line", "Ankara–Kayseri–Sivas–Erzurum Route", ColorIndigo,
                "LINESTRING(32.86 39.93, 35.49 38.72, 37.02 39.75, 41.27 39.90)", 72),

            // Admin: nine visible tourism/regional zones.
            new("admin", "polygon", "Historic Istanbul Zone", ColorBlue,
                "POLYGON((28.93 40.98, 29.06 40.98, 29.06 41.08, 28.93 41.08, 28.93 40.98))", 89),
            new("admin", "polygon", "Cappadocia Explorer Zone", ColorGreen,
                "POLYGON((34.70 38.55, 35.05 38.55, 35.05 38.85, 34.70 38.85, 34.70 38.55))", 83),
            new("admin", "polygon", "Pamukkale Explorer Zone", ColorOrange,
                "POLYGON((29.05 37.85, 29.20 37.85, 29.20 37.98, 29.05 37.98, 29.05 37.85))", 77),
            new("admin", "polygon", "Antalya Old Town Zone", ColorAmber,
                "POLYGON((30.67 36.86, 30.74 36.86, 30.74 36.93, 30.67 36.93, 30.67 36.86))", 71),
            new("admin", "polygon", "Göbeklitepe Explorer Zone", ColorRed,
                "POLYGON((38.85 37.18, 38.98 37.18, 38.98 37.28, 38.85 37.28, 38.85 37.18))", 65),
            new("admin", "polygon", "Lake Van Explorer Zone", ColorViolet,
                "POLYGON((42.50 38.20, 43.60 38.20, 43.60 39.00, 42.50 39.00, 42.50 38.20))", 59),
            new("admin", "polygon", "Black Sea Highlands Zone", ColorCyan,
                "POLYGON((39.20 40.70, 40.20 40.70, 40.20 41.30, 39.20 41.30, 39.20 40.70))", 53),
            new("admin", "polygon", "Uludağ Explorer Zone", ColorBlue,
                "POLYGON((28.95 39.95, 29.35 39.95, 29.35 40.25, 28.95 40.25, 28.95 39.95))", 47),
            new("admin", "polygon", "Mardin Old City Zone", ColorRed,
                "POLYGON((40.68 37.28, 40.80 37.28, 40.80 37.36, 40.68 37.36, 40.68 37.28))", 41),
        };

        ManagerSeed[] managers =
        [
            new("marmara_manager", "Marmara", 29.00, 40.70, ColorBlue),
            new("aegean_manager", "Aegean", 27.80, 38.20, ColorOrange),
            new("mediterranean_manager", "Mediterranean", 33.00, 36.90, ColorAmber),
            new("central_manager", "Central Anatolia", 33.00, 39.20, ColorGreen),
            new("blacksea_manager", "Black Sea", 36.00, 41.10, ColorCyan),
            new("eastern_manager", "Eastern Anatolia", 41.00, 39.20, ColorViolet),
            new("southeast_manager", "Southeastern Anatolia", 38.50, 37.30, ColorRed),
        ];

        for (var i = 0; i < managers.Length; i++)
            rows.AddRange(BuildManagerShapes(managers[i], 38 - i * 2));

        // Editors: Ankara demonstrates a direct polygon grant; the others only own point/line rows.
        rows.AddRange(
        [
            new("ankara_editor", "point", "Ankara Editorial Desk", ColorIndigo, "POINT(32.85 39.93)", 30),
            new("ankara_editor", "point", "Çankaya Review Point", ColorIndigo, "POINT(32.86 39.89)", 28),
            new("ankara_editor", "line", "Ankara City Walk", ColorBlue,
                "LINESTRING(32.82 39.92, 32.85 39.93, 32.88 39.94)", 26),
            new("ankara_editor", "line", "Ankara Museum Route", ColorBlue,
                "LINESTRING(32.84 39.91, 32.86 39.94, 32.88 39.96)", 24),
            new("ankara_editor", "polygon", "Ankara Culture Review Zone", ColorGreen,
                "POLYGON((32.80 39.88, 32.90 39.88, 32.90 39.96, 32.80 39.96, 32.80 39.88))", 22),
            new("ankara_editor", "polygon", "Ankara Event Review Zone", ColorGreen,
                "POLYGON((32.70 39.90, 32.78 39.90, 32.78 39.98, 32.70 39.98, 32.70 39.90))", 20),

            new("istanbul_editor", "point", "Istanbul Editorial Desk", ColorIndigo, "POINT(28.98 41.03)", 18),
            new("istanbul_editor", "line", "Istanbul Editorial Walk", ColorBlue,
                "LINESTRING(28.95 41.01, 28.98 41.03, 29.02 41.05)", 17),
            new("izmir_editor", "point", "Izmir Editorial Desk", ColorIndigo, "POINT(27.14 38.42)", 16),
            new("izmir_editor", "line", "Izmir Editorial Walk", ColorBlue,
                "LINESTRING(27.11 38.40, 27.14 38.42, 27.17 38.44)", 15),
            new("antalya_editor", "point", "Antalya Editorial Desk", ColorIndigo, "POINT(30.71 36.90)", 14),
            new("antalya_editor", "line", "Antalya Editorial Walk", ColorBlue,
                "LINESTRING(30.68 36.88, 30.71 36.90, 30.75 36.92)", 13),

            // Viewer rows demonstrate read access while write endpoints and controls remain locked.
            new("viewer", "point", "Saved Favorite: Cappadocia", ColorRed, "POINT(34.86 38.64)", 12),
            new("viewer", "point", "Saved Favorite: Ephesus", ColorRed, "POINT(27.34 37.94)", 11),
            new("viewer", "line", "Saved Turkey Journey", ColorOrange,
                "LINESTRING(28.98 41.01, 32.86 39.93, 34.86 38.64, 30.71 36.90)", 10),
        ]);

        return rows;
    }

    private static IEnumerable<DemoShape> BuildManagerShapes(ManagerSeed seed, int daysAgo)
    {
        var x = seed.Lon;
        var y = seed.Lat;
        return
        [
            new(seed.Owner, "point", $"{seed.Label} Regional Desk", seed.Color,
                $"POINT({F(x)} {F(y)})", daysAgo),
            new(seed.Owner, "point", $"{seed.Label} Visitor Point", seed.Color,
                $"POINT({F(x + 0.18)} {F(y + 0.08)})", daysAgo - 1),
            new(seed.Owner, "point", $"{seed.Label} Scenic Point", seed.Color,
                $"POINT({F(x - 0.16)} {F(y - 0.08)})", daysAgo - 2),
            new(seed.Owner, "line", $"{seed.Label} Discovery Route", seed.Color,
                $"LINESTRING({F(x - 0.24)} {F(y - 0.08)}, {F(x)} {F(y)}, {F(x + 0.24)} {F(y + 0.10)})",
                daysAgo - 3),
            new(seed.Owner, "line", $"{seed.Label} Cultural Route", seed.Color,
                $"LINESTRING({F(x - 0.18)} {F(y + 0.14)}, {F(x + 0.02)} {F(y + 0.04)}, {F(x + 0.20)} {F(y - 0.10)})",
                daysAgo - 4),
            new(seed.Owner, "polygon", $"{seed.Label} Tourism Zone", seed.Color,
                SquareWkt(x - 0.12, y - 0.10, 0.18), daysAgo - 5),
            new(seed.Owner, "polygon", $"{seed.Label} Event Zone", seed.Color,
                SquareWkt(x + 0.04, y + 0.02, 0.16), daysAgo - 6),
        ];
    }

    private static string SquareWkt(double x, double y, double size) =>
        $"POLYGON(({F(x)} {F(y)}, {F(x + size)} {F(y)}, {F(x + size)} {F(y + size)}, " +
        $"{F(x)} {F(y + size)}, {F(x)} {F(y)}))";

    private static string F(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    public sealed record DemoCategory(
        string Name,
        string? Parent,
        string? Color = null,
        string? IconKey = null);

    public static readonly IReadOnlyList<DemoCategory> Categories =
    [
        new("Food & Drink", null, ColorOrange, "food"),
        new("Restaurant", "Food & Drink"),
        new("Cafe", "Food & Drink", IconKey: "coffee"),
        new("Bakery", "Food & Drink", IconKey: "bakery"),
        new("Fast Food", "Food & Drink"),

        new("Health", null, ColorRed, "health"),
        new("Hospital", "Health"),
        new("Pharmacy", "Health", IconKey: "pharmacy"),
        new("24/7 Pharmacy", "Health", IconKey: "pharmacy"),

        new("Shopping", null, ColorBlue, "shopping"),
        new("Mall", "Shopping"),
        new("Supermarket", "Shopping"),

        new("Culture & Tourism", null, ColorViolet, "culture"),
        new("Museum", "Culture & Tourism", IconKey: "museum"),
        new("Historical Site", "Culture & Tourism"),
        new("Hotel", "Culture & Tourism", IconKey: "hotel"),
        new("Visitor Center", "Culture & Tourism"),
        new("Art Gallery", "Culture & Tourism"),

        new("Services", null, ColorGreen, "services"),
        new("Bank", "Services", IconKey: "bank"),
        new("Gas Station", "Services", IconKey: "fuel"),
        new("Post Office", "Services", IconKey: "mail"),
        new("Municipality", "Services", IconKey: "government"),

        new("Transport", null, ColorIndigo, "transport"),
        new("Airport", "Transport", IconKey: "airport"),
        new("Train Station", "Transport"),
        new("Bus Terminal", "Transport"),
        new("Ferry Terminal", "Transport"),
        new("Metro Station", "Transport"),

        new("Education", null, ColorCyan, "education"),
        new("University", "Education"),
        new("Library", "Education"),
        new("High School", "Education"),

        new("Nature & Recreation", null, ColorGreen, "nature"),
        new("National Park", "Nature & Recreation"),
        new("Beach", "Nature & Recreation"),
        new("Park", "Nature & Recreation"),
        new("Botanical Garden", "Nature & Recreation"),

        new("Sports", null, ColorAmber, "sports"),
        new("Stadium", "Sports"),
        new("Ski Center", "Sports"),
        new("Gym", "Sports"),
    ];

    // Every province seeds one admin-owned public institution; the kind rotates by province index
    // so the nationwide layer mixes hospitals, terminals, museums, parks… instead of one repeated
    // template. Names are Turkish ("Bursa Şehir Hastanesi") because these institutions are named
    // that way in the real world; curated landmarks below keep their real names.
    public sealed record DemoProvincePoi(string NameSuffix, string Category, TimeOnly Open, TimeOnly Close);

    public static readonly IReadOnlyList<DemoProvincePoi> ProvincePoiTemplates =
    [
        new("Şehir Hastanesi", "Hospital", new(0, 0), new(23, 59)),
        new("Otogarı", "Bus Terminal", new(5, 0), new(23, 59)),
        new("Merkez Postanesi", "Post Office", new(8, 30), new(17, 30)),
        new("Belediyesi", "Municipality", new(8, 30), new(17, 30)),
        new("Kent Müzesi", "Museum", new(9, 0), new(18, 0)),
        new("Şehir Stadyumu", "Stadium", new(9, 0), new(22, 0)),
        new("Halk Kütüphanesi", "Library", new(9, 0), new(21, 0)),
        new("Şehir Parkı", "Park", new(6, 0), new(23, 0)),
        new("Garı", "Train Station", new(5, 0), new(23, 59)),
        new("Merkez Eczanesi", "Pharmacy", new(8, 0), new(22, 0)),
        new("Merkez Banka Şubesi", "Bank", new(9, 0), new(17, 30)),
        new("Akaryakıt İstasyonu", "Gas Station", new(0, 0), new(23, 59)),
    ];

    // Single source of truth for the generated province POI: DemoSeeder uses this both to insert
    // and to verify, so the rotation can never drift between the two.
    public static (string Name, string Category, TimeOnly Open, TimeOnly Close) ProvincePoiFor(
        int provinceIndex, string provinceName)
    {
        var template = ProvincePoiTemplates[provinceIndex % ProvincePoiTemplates.Count];
        return ($"{provinceName} {template.NameSuffix}", template.Category, template.Open, template.Close);
    }

    public sealed record DemoPoi(
        string Owner,
        string Name,
        string Category,
        string Wkt,
        TimeOnly Open,
        TimeOnly Close,
        int DaysAgo);

    private static TimeOnly At(int hour, int minute) => new(hour, minute);

    // 58 curated hotspots: real landmarks and plausible businesses concentrated in the cities the
    // demo personas operate in. Together with the 81 generated province institutions this yields
    // exactly 139 POIs, and every leaf category is used at least once across the two sets.
    // Operator-owned entries must sit inside that operator's authorization area (the seeder
    // preflight verifies this with Covers()).
    public static readonly IReadOnlyList<DemoPoi> Pois =
    [
        // Istanbul operator: five POIs inside the Istanbul authorization area.
        new("istanbul_operator", "Hafız Mustafa 1864", "Bakery", "POINT(28.974 41.016)", At(7, 0), At(23, 0), 80),
        new("istanbul_operator", "Moda 24/7 Pharmacy", "24/7 Pharmacy", "POINT(29.025 40.983)", At(0, 0), At(23, 59), 78),
        new("istanbul_operator", "Zorlu Center", "Mall", "POINT(29.016 41.065)", At(10, 0), At(22, 0), 76),
        new("istanbul_operator", "Kadıköy Ferry Terminal", "Ferry Terminal", "POINT(29.023 40.991)", At(5, 30), At(23, 30), 74),
        new("istanbul_operator", "Sultanahmet Restaurant", "Restaurant", "POINT(28.976 41.008)", At(11, 0), At(22, 0), 72),

        // Antalya operator.
        new("antalya_operator", "Konyaaltı Beach", "Beach", "POINT(30.650 36.870)", At(6, 0), At(23, 0), 70),
        new("antalya_operator", "Antalya Explorer Hotel", "Hotel", "POINT(30.705 36.885)", At(0, 0), At(23, 59), 68),
        new("antalya_operator", "Antalya Airport", "Airport", "POINT(30.800 36.899)", At(0, 0), At(23, 59), 66),
        new("antalya_operator", "Antalya Stadium", "Stadium", "POINT(30.668 36.888)", At(9, 0), At(22, 0), 64),
        new("antalya_operator", "Düden Nature Park", "National Park", "POINT(30.770 36.850)", At(8, 0), At(20, 0), 62),

        // Gaziantep operator.
        new("gaziantep_operator", "Gaziantep Local Restaurant", "Restaurant", "POINT(37.379 37.065)", At(11, 0), At(23, 0), 60),
        new("gaziantep_operator", "Gaziantep City Hospital", "Hospital", "POINT(37.350 37.080)", At(0, 0), At(23, 59), 58),
        new("gaziantep_operator", "Gaziantep Supermarket", "Supermarket", "POINT(37.390 37.050)", At(8, 0), At(22, 0), 56),
        new("gaziantep_operator", "Zeugma Mosaic Museum", "Museum", "POINT(37.391 37.076)", At(9, 0), At(19, 0), 54),
        new("gaziantep_operator", "Gaziantep Municipality", "Municipality", "POINT(37.378 37.061)", At(8, 30), At(17, 30), 52),

        // Trabzon operator.
        new("trabzon_operator", "Trabzon Seafront Cafe", "Cafe", "POINT(39.730 41.003)", At(8, 0), At(23, 0), 50),
        new("trabzon_operator", "Trabzon Gas Station", "Gas Station", "POINT(39.690 40.995)", At(0, 0), At(23, 59), 48),
        new("trabzon_operator", "Trabzon University", "University", "POINT(39.765 40.992)", At(8, 0), At(20, 0), 46),
        new("trabzon_operator", "Trabzon Coastal Park", "Park", "POINT(39.710 41.005)", At(6, 0), At(23, 0), 44),
        new("trabzon_operator", "Trabzon Historical House", "Historical Site", "POINT(39.722 41.006)", At(9, 0), At(18, 0), 42),

        // Seven admin hotspots use the remaining leaf categories.
        new("admin", "Ankara Central Pharmacy", "Pharmacy", "POINT(32.855 39.921)", At(8, 0), At(22, 0), 40),
        new("admin", "Ankara City Library", "Library", "POINT(32.846 39.934)", At(9, 0), At(21, 0), 38),
        new("admin", "İzmir Central Bank", "Bank", "POINT(27.138 38.426)", At(9, 0), At(17, 30), 36),
        new("admin", "İzmir Train Station", "Train Station", "POINT(27.151 38.425)", At(5, 0), At(23, 59), 34),
        new("admin", "Bursa Central Post Office", "Post Office", "POINT(29.061 40.184)", At(8, 30), At(17, 30), 32),
        new("admin", "Adana Bus Terminal", "Bus Terminal", "POINT(35.280 37.020)", At(0, 0), At(23, 59), 30),
        new("admin", "Konya Ski Center", "Ski Center", "POINT(32.480 37.890)", At(8, 0), At(18, 0), 28),

        // Istanbul landmarks (istanbul_operator's authorization area).
        new("istanbul_operator", "Galata Kulesi", "Historical Site", "POINT(28.974 41.026)", At(9, 0), At(20, 0), 27),
        new("istanbul_operator", "Kapalıçarşı", "Mall", "POINT(28.968 41.011)", At(9, 0), At(19, 0), 26),
        new("istanbul_operator", "İstanbul Modern", "Art Gallery", "POINT(28.982 41.022)", At(10, 0), At(18, 0), 25),
        new("istanbul_operator", "Taksim Metro İstasyonu", "Metro Station", "POINT(28.985 41.037)", At(6, 0), At(23, 59), 24),
        new("istanbul_operator", "Bosphorus Burger", "Fast Food", "POINT(29.027 41.047)", At(11, 0), At(23, 0), 23),
        new("istanbul_operator", "Gülhane Parkı", "Park", "POINT(28.981 41.013)", At(6, 0), At(22, 0), 22),
        new("istanbul_operator", "Galatasaray Lisesi", "High School", "POINT(28.978 41.032)", At(8, 0), At(17, 0), 21),

        // Ankara landmarks (admin-owned; no operator covers Ankara).
        new("admin", "Anıtkabir", "Historical Site", "POINT(32.837 39.925)", At(9, 0), At(17, 0), 20),
        new("admin", "CerModern", "Art Gallery", "POINT(32.849 39.937)", At(10, 0), At(18, 0), 19),
        new("admin", "Ankamall", "Mall", "POINT(32.831 39.951)", At(10, 0), At(22, 0), 18),
        new("admin", "Kızılay Metro İstasyonu", "Metro Station", "POINT(32.854 39.920)", At(6, 0), At(23, 59), 17),
        new("admin", "ODTÜ", "University", "POINT(32.777 39.891)", At(8, 0), At(18, 0), 16),
        new("admin", "Ankara Botanik Parkı", "Botanical Garden", "POINT(32.810 39.910)", At(7, 0), At(21, 0), 15),

        // İzmir landmarks.
        new("admin", "İzmir Saat Kulesi", "Historical Site", "POINT(27.129 38.419)", At(0, 0), At(23, 59), 14),
        new("admin", "Kültürpark", "Park", "POINT(27.145 38.431)", At(6, 0), At(23, 0), 13),
        new("admin", "Adnan Menderes Havalimanı", "Airport", "POINT(27.155 38.292)", At(0, 0), At(23, 59), 12),

        // Bursa and Eskişehir.
        new("admin", "Uludağ Kayak Merkezi", "Ski Center", "POINT(29.171 40.099)", At(8, 0), At(17, 0), 11),
        new("admin", "Koza Han Çarşısı", "Mall", "POINT(29.062 40.183)", At(8, 30), At(20, 0), 10),
        new("admin", "Sazova Parkı", "Park", "POINT(30.470 39.762)", At(6, 0), At(23, 0), 9),
        new("admin", "Porsuk Spor Salonu", "Gym", "POINT(30.520 39.777)", At(7, 0), At(23, 0), 8),

        // Cappadocia, Samsun, and the Muğla coast.
        new("admin", "Göreme Ziyaretçi Merkezi", "Visitor Center", "POINT(34.829 38.643)", At(8, 0), At(19, 0), 7),
        new("admin", "Göreme Açık Hava Müzesi", "Museum", "POINT(34.841 38.642)", At(8, 0), At(19, 0), 6),
        new("admin", "Samsun Fener Plajı", "Beach", "POINT(36.330 41.300)", At(6, 0), At(22, 0), 5),
        new("admin", "Ölüdeniz Plajı", "Beach", "POINT(29.116 36.550)", At(6, 0), At(22, 0), 4),
        new("admin", "Bodrum Kalesi", "Historical Site", "POINT(27.427 37.032)", At(8, 30), At(18, 30), 3),

        // Extra operator hotspots inside their areas.
        new("antalya_operator", "Aspendos Antik Tiyatrosu", "Historical Site", "POINT(31.172 36.939)", At(8, 0), At(19, 0), 43),
        new("antalya_operator", "Konyaaltı Spor Salonu", "Gym", "POINT(30.637 36.874)", At(7, 0), At(23, 0), 41),
        new("gaziantep_operator", "Gaziantep Botanik Bahçesi", "Botanical Garden", "POINT(37.349 37.075)", At(8, 0), At(19, 0), 39),
        new("gaziantep_operator", "İmam Çağdaş", "Restaurant", "POINT(37.383 37.063)", At(11, 0), At(22, 0), 37),
        new("trabzon_operator", "Sümela Manastırı", "Historical Site", "POINT(39.658 40.690)", At(9, 0), At(18, 0), 35),
        new("trabzon_operator", "Uzungöl", "National Park", "POINT(40.293 40.618)", At(0, 0), At(23, 59), 33),
    ];
}
