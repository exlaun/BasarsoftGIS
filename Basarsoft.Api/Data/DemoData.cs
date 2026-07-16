namespace Basarsoft.Api.Data;

// The demo scenario, as plain data. No EF, no I/O — DemoSeeder turns this into rows.
//
// The story: a national fiber operator. Field teams maintain cabinets, splice closures and base
// stations (points), backbone and ring routes (lines), and service/maintenance zones (polygons);
// a shared POI catalogue holds the customer-facing stores and service centers (which is what makes
// their opening hours meaningful). Each team is limited to the region it is authorized for.
//
// Everything is WGS84 lon/lat (EPSG:4326), lon first — the same wire format the client draws in.
public static class DemoData
{
    // Every demo account logs in with this password.
    public const string Password = "secret123";

    // --- Accounts ------------------------------------------------------------------------------
    //
    // Each persona exists to demonstrate exactly one thing (see Demonstrates). Drawings are private
    // per user in this system, so an account with no shapes of its own would show an empty map —
    // hence every persona that needs a map has shapes below.

    public sealed record DemoUser(
        string Username,
        string Role,
        string[] DirectPermissions,
        string? AreaWkt,
        string Demonstrates);

    public static readonly IReadOnlyList<DemoUser> Users = new[]
    {
        new DemoUser("admin", SeedData.AdminRoleName, [], null,
            "Full access: heat map, query paging, analysis, the whole admin panel"),

        new DemoUser("marmara.op", FieldOperatorRoleName, [], null,
            "Geographic limit inherited from the role (Marmara)"),

        // The centrepiece of the geo-authorization demo. He holds the same Field Operator role as
        // marmara.op, but his OWN area overrides the role's — and it is a disjoint region, so the
        // override is obvious on the map instead of being a subtle narrowing.
        new DemoUser("antalya.op", FieldOperatorRoleName, [], AntalyaAreaWkt,
            "A user-level area OVERRIDES the role's area (Antalya, not Marmara)"),

        // His role grants add_point; add_polygon is granted directly to him. The admin panel's
        // permission modal shows the first locked with a "From role: Surveyor" badge and the second
        // ticked as "Direct" — the inheritance rule, visible.
        new DemoUser("surveyor", SurveyorRoleName, ["add_polygon"], null,
            "Role-inherited vs. directly-granted permissions"),

        new DemoUser("poi.op", SeedData.OperatorRoleName, [], null,
            "Permission-scoped toolbar: the POI tool only, no drawing tools"),

        new DemoUser("viewer", SeedData.ViewerRoleName, [], null,
            "Read-only: no draw tools, no admin link"),
    };

    // --- Roles ---------------------------------------------------------------------------------
    //
    // Admin / Operator / Viewer already come from AdminSeeder; these two are the scenario's own.

    public const string FieldOperatorRoleName = "Field Operator";
    public const string SurveyorRoleName = "Surveyor";

    public sealed record DemoRole(string Name, string Description, string[] Permissions, string? AreaWkt);

    public static readonly IReadOnlyList<DemoRole> Roles = new[]
    {
        new DemoRole(FieldOperatorRoleName, "Maintains fiber assets in an assigned region",
            ["add_point", "add_line", "add_polygon"], MarmaraAreaWkt),

        new DemoRole(SurveyorRoleName, "Records survey markers; may not lay routes",
            ["add_point"], null),
    };

    // --- Authorization areas -------------------------------------------------------------------
    //
    // Single valid POLYGONs (the column is geometry(Polygon,4326) and the service rejects anything
    // else). Every shape owned by a user these apply to must lie fully inside — the check is
    // Covers(), and it fires on create/update but never on read, so a mis-placed shape would be
    // visible yet silently un-editable. DemoSeeder asserts this before committing.

    // Marmara: covers İstanbul, Bursa, Kocaeli, Sakarya, Tekirdağ.
    private const string MarmaraAreaWkt =
        "POLYGON((26.30 40.10, 27.20 39.55, 30.20 39.55, 30.95 40.35, 30.95 41.20, " +
        "30.10 41.75, 27.60 41.75, 26.30 41.10, 26.30 40.10))";

    // Antalya: the Mediterranean coast from Kemer to Manavgat. Deliberately disjoint from Marmara.
    private const string AntalyaAreaWkt =
        "POLYGON((29.95 36.60, 30.70 36.20, 31.75 36.45, 31.95 37.10, 31.20 37.50, " +
        "30.25 37.30, 29.95 36.60))";

    // --- Shapes --------------------------------------------------------------------------------
    //
    // Owner is a username from Users. Type is point/line/polygon. Color is never null: a null color
    // renders as the default blue in both the client's WFS styling and the GeoServer SLDs, which
    // would flatten the whole map to one shade. DaysAgo spreads CreatedAt over ~3 months so sorting
    // the query panel by date means something.

    public sealed record DemoShape(
        string Owner,
        string Type,
        string Name,
        string Color,
        string Wkt,
        int DaysAgo);

    private const string ColorCabinet = "#f59e0b";   // amber  — street cabinets
    private const string ColorSplice = "#dc2626";    // red    — splice closures
    private const string ColorStation = "#7c3aed";   // violet — base stations
    private const string ColorNode = "#0891b2";      // cyan   — core / regional nodes
    private const string ColorBackbone = "#16a34a";  // green  — long-haul routes
    private const string ColorZone = "#2563eb";      // blue   — service zones

    public static readonly IReadOnlyList<DemoShape> Shapes = new[]
    {
        // ===== admin: 35 shapes, placed for the heat map =========================================
        // vw_heat collapses every feature to one point (points as-is, lines by centroid, polygons by
        // point-on-surface) and renders it per-user. The density below is what produces a red core
        // over İstanbul grading down to isolated blue dots in the east.

        // --- İstanbul: 13 features inside ±0.05°, the hot core -----------------------------------
        new DemoShape("admin", "point", "Splice Closure IST-01", ColorSplice, "POINT(28.955 41.005)", 88),
        new DemoShape("admin", "point", "Splice Closure IST-02", ColorSplice, "POINT(28.972 41.021)", 86),
        new DemoShape("admin", "point", "Street Cabinet IST-03", ColorCabinet, "POINT(28.991 41.033)", 84),
        new DemoShape("admin", "point", "Street Cabinet IST-04", ColorCabinet, "POINT(29.012 41.014)", 81),
        new DemoShape("admin", "point", "Base Station IST-05", ColorStation, "POINT(28.944 41.028)", 77),
        new DemoShape("admin", "point", "Base Station IST-06", ColorStation, "POINT(29.024 40.996)", 74),
        new DemoShape("admin", "point", "Splice Closure IST-07", ColorSplice, "POINT(28.967 40.981)", 70),
        new DemoShape("admin", "point", "Street Cabinet IST-08", ColorCabinet, "POINT(29.001 40.972)", 66),
        new DemoShape("admin", "point", "Core Node IST-09", ColorNode, "POINT(28.985 41.010)", 63),
        new DemoShape("admin", "line", "İstanbul Metro Ring West", ColorNode,
            "LINESTRING(28.940 41.020, 28.968 41.035, 28.998 41.040, 29.020 41.025)", 59),
        new DemoShape("admin", "line", "İstanbul Metro Ring East", ColorNode,
            "LINESTRING(29.010 40.985, 28.985 40.968, 28.955 40.975)", 57),
        // Contains all nine İstanbul points — draw a polygon here during the demo and the "contained
        // shapes" count comes back non-zero.
        new DemoShape("admin", "polygon", "İstanbul Core Service Zone", ColorZone,
            "POLYGON((28.930 40.960, 29.040 40.960, 29.040 41.050, 28.930 41.050, 28.930 40.960))", 90),
        // Deliberately nested inside the zone above: two overlapping polygons under one click is what
        // the shape-picker modal exists for.
        new DemoShape("admin", "polygon", "İstanbul Fiber Maintenance Area", ColorStation,
            "POLYGON((28.950 40.990, 29.010 40.990, 29.010 41.030, 28.950 41.030, 28.950 40.990))", 45),

        // --- Ankara: 7 features spread over ~0.4°, a warm spot -----------------------------------
        new DemoShape("admin", "point", "Street Cabinet ANK-01", ColorCabinet, "POINT(32.855 39.925)", 82),
        new DemoShape("admin", "point", "Splice Closure ANK-02", ColorSplice, "POINT(32.735 39.985)", 76),
        new DemoShape("admin", "point", "Base Station ANK-03", ColorStation, "POINT(33.010 39.860)", 68),
        new DemoShape("admin", "point", "Core Node ANK-04", ColorNode, "POINT(32.640 39.780)", 61),
        new DemoShape("admin", "line", "Ankara Ring Backbone", ColorNode,
            "LINESTRING(32.640 39.780, 32.855 39.925, 33.010 39.860)", 54),
        new DemoShape("admin", "line", "Ankara North Feeder", ColorNode,
            "LINESTRING(32.735 39.985, 32.900 40.060, 33.080 40.100)", 48),
        new DemoShape("admin", "polygon", "Ankara Maintenance Area", ColorZone,
            "POLYGON((32.60 39.75, 33.10 39.75, 33.10 40.05, 32.60 40.05, 32.60 39.75))", 87),

        // --- İzmir: 5 features, a cooler warm spot -----------------------------------------------
        new DemoShape("admin", "point", "Street Cabinet IZM-01", ColorCabinet, "POINT(27.142 38.423)", 72),
        new DemoShape("admin", "point", "Splice Closure IZM-02", ColorSplice, "POINT(27.075 38.465)", 65),
        new DemoShape("admin", "point", "Base Station IZM-03", ColorStation, "POINT(27.215 38.380)", 58),
        new DemoShape("admin", "line", "İzmir Coastal Ring", ColorNode,
            "LINESTRING(27.075 38.465, 27.142 38.423, 27.215 38.380)", 44),
        new DemoShape("admin", "polygon", "İzmir Service Zone", ColorZone,
            "POLYGON((27.02 38.34, 27.28 38.34, 27.28 38.52, 27.02 38.52, 27.02 38.34))", 80),

        // --- The long-haul backbone: what ties the regions together ------------------------------
        new DemoShape("admin", "line", "İstanbul–Ankara Fiber Backbone", ColorBackbone,
            "LINESTRING(28.98 41.01, 30.40 40.77, 31.60 40.30, 32.86 39.93)", 89),
        new DemoShape("admin", "line", "Ankara–İzmir Fiber Backbone", ColorBackbone,
            "LINESTRING(32.86 39.93, 31.10 39.20, 29.40 38.80, 27.14 38.42)", 85),
        new DemoShape("admin", "line", "Ankara–Adana Fiber Backbone", ColorBackbone,
            "LINESTRING(32.86 39.93, 33.60 38.60, 34.70 37.30, 35.33 37.00)", 79),

        // --- Isolated regional nodes: the cool blue dots on the heat map -------------------------
        new DemoShape("admin", "point", "Regional Node ANT-01", ColorNode, "POINT(30.713 36.884)", 40),
        new DemoShape("admin", "point", "Regional Node KON-01", ColorNode, "POINT(32.492 37.874)", 36),
        new DemoShape("admin", "point", "Regional Node GAZ-01", ColorNode, "POINT(37.383 37.066)", 31),
        new DemoShape("admin", "point", "Regional Node TRB-01", ColorNode, "POINT(39.717 41.005)", 27),
        new DemoShape("admin", "point", "Regional Node ERZ-01", ColorNode, "POINT(41.277 39.904)", 22),
        new DemoShape("admin", "point", "Regional Node VAN-01", ColorNode, "POINT(43.383 38.494)", 18),
        new DemoShape("admin", "polygon", "Southeast Planned Coverage", ColorStation,
            "POLYGON((36.80 36.60, 38.20 36.60, 38.20 37.60, 36.80 37.60, 36.80 36.60))", 33),

        // ===== marmara.op: 6 shapes, every one inside the Marmara role area =====================
        new DemoShape("marmara.op", "point", "Street Cabinet TKD-04", ColorCabinet, "POINT(27.510 40.980)", 52),
        new DemoShape("marmara.op", "point", "Splice Closure IZT-11", ColorSplice, "POINT(29.920 40.770)", 47),
        new DemoShape("marmara.op", "point", "Base Station BRS-07", ColorStation, "POINT(29.060 40.190)", 41),
        new DemoShape("marmara.op", "line", "Bursa–Kocaeli Distribution Route", ColorNode,
            "LINESTRING(29.060 40.190, 29.450 40.450, 29.920 40.770)", 35),
        new DemoShape("marmara.op", "line", "Tekirdağ Coastal Feeder", ColorNode,
            "LINESTRING(27.510 40.980, 28.100 41.050, 28.620 41.120)", 29),
        // Contains his own Bursa base station.
        new DemoShape("marmara.op", "polygon", "Marmara South Maintenance Zone", ColorZone,
            "POLYGON((28.60 40.05, 29.60 40.05, 29.60 40.55, 28.60 40.55, 28.60 40.05))", 56),

        // ===== antalya.op: 5 shapes, every one inside HIS OWN area (not the role's) ==============
        new DemoShape("antalya.op", "point", "Street Cabinet ANT-02", ColorCabinet, "POINT(30.710 36.890)", 50),
        new DemoShape("antalya.op", "point", "Splice Closure MNV-05", ColorSplice, "POINT(31.440 36.830)", 43),
        new DemoShape("antalya.op", "point", "Base Station KMR-03", ColorStation, "POINT(30.570 36.650)", 38),
        new DemoShape("antalya.op", "line", "Antalya–Manavgat Coastal Backbone", ColorBackbone,
            "LINESTRING(30.710 36.890, 31.050 36.850, 31.440 36.830)", 32),
        // Contains his own Antalya cabinet.
        new DemoShape("antalya.op", "polygon", "Antalya Bay Service Zone", ColorZone,
            "POLYGON((30.45 36.72, 31.10 36.72, 31.10 37.05, 30.45 37.05, 30.45 36.72))", 55),

        // ===== surveyor: points + polygons, and no lines — he holds no add_line ==================
        new DemoShape("surveyor", "point", "Survey Marker KON-01", ColorNode, "POINT(32.480 37.870)", 26),
        new DemoShape("surveyor", "point", "Survey Marker KON-02", ColorNode, "POINT(32.550 37.920)", 24),
        new DemoShape("surveyor", "point", "Survey Marker KON-03", ColorNode, "POINT(32.410 37.950)", 21),
        new DemoShape("surveyor", "point", "Survey Marker ESK-01", ColorNode, "POINT(30.520 39.780)", 17),
        new DemoShape("surveyor", "point", "Survey Marker AFY-01", ColorNode, "POINT(30.540 38.760)", 14),
        // Contains all three Konya markers.
        new DemoShape("surveyor", "polygon", "Konya Survey Grid", ColorZone,
            "POLYGON((32.35 37.80, 32.65 37.80, 32.65 38.00, 32.35 38.00, 32.35 37.80))", 20),
        new DemoShape("surveyor", "polygon", "Eskişehir Planned Coverage", ColorZone,
            "POLYGON((30.35 39.65, 30.75 39.65, 30.75 39.92, 30.35 39.92, 30.35 39.65))", 12),
    };

    // --- POI category tree ---------------------------------------------------------------------
    //
    // Three levels deep, so the breadcrumb the POI list renders ("Customer Service > Retail >
    // Flagship Store") actually exercises the path builder. Order matters: a parent must appear
    // before any child that names it.
    //
    // Colors drive the per-category marker styling (GeoServer SLD + client style). Null = inherit
    // the nearest colored ancestor; a chain that is null all the way up falls back to the default
    // POI rose. The nulls below are deliberate demo material: "Drop-off Point" inherits Technical's
    // teal, the "Operations > Depot" leaves show the fallback.

    public sealed record DemoCategory(string Name, string? Parent, string? Color = null);

    public static readonly IReadOnlyList<DemoCategory> Categories = new[]
    {
        new DemoCategory("Customer Service", null),
        new DemoCategory("Retail", "Customer Service"),
        new DemoCategory("Flagship Store", "Retail", "#7c3aed"),
        new DemoCategory("Authorized Dealer", "Retail", "#0ea5e9"),
        new DemoCategory("Technical", "Customer Service", "#0891b2"),
        new DemoCategory("Service Center", "Technical", "#f59e0b"),
        // No color of its own -> inherits Technical's #0891b2 (proves inheritance in the demo).
        new DemoCategory("Drop-off Point", "Technical"),
        new DemoCategory("Operations", null),
        new DemoCategory("Depot", "Operations"),
        // Colorless chain all the way to the root -> renders in the default POI rose (fallback).
        new DemoCategory("Regional Warehouse", "Depot"),
        // Deliberately left without POIs, so the admin tree's count column isn't uniform — and so
        // there is a category that can actually be deleted during the demo (the service refuses to
        // delete one that still has children or POIs).
        new DemoCategory("Field Office", "Operations"),
    };

    // --- POIs ----------------------------------------------------------------------------------
    //
    // One shared catalogue: every account sees all of them, whatever their role. Authored by a mix
    // of admin and poi.op so the admin list's "Added by" column isn't a single repeated name.
    // Neither of those two has an authorization area, so POIs may sit anywhere in the country.

    public sealed record DemoPoi(
        string Owner,
        string Name,
        string Category,
        string Wkt,
        TimeOnly Open,
        TimeOnly Close,
        int DaysAgo);

    private static TimeOnly At(int hour, int minute) => new(hour, minute);

    public static readonly IReadOnlyList<DemoPoi> Pois = new[]
    {
        new DemoPoi("admin", "Flagship Store — İstanbul Nişantaşı", "Flagship Store",
            "POINT(28.995 41.048)", At(10, 0), At(22, 0), 62),
        new DemoPoi("admin", "Flagship Store — Ankara Kızılay", "Flagship Store",
            "POINT(32.854 39.920)", At(10, 0), At(21, 0), 60),
        new DemoPoi("poi.op", "Flagship Store — İzmir Alsancak", "Flagship Store",
            "POINT(27.142 38.437)", At(10, 0), At(22, 0), 57),

        new DemoPoi("poi.op", "Authorized Dealer — Kadıköy", "Authorized Dealer",
            "POINT(29.026 40.990)", At(9, 0), At(19, 0), 53),
        new DemoPoi("admin", "Authorized Dealer — Beşiktaş", "Authorized Dealer",
            "POINT(29.006 41.043)", At(9, 0), At(19, 0), 51),
        new DemoPoi("poi.op", "Authorized Dealer — Bursa Osmangazi", "Authorized Dealer",
            "POINT(29.061 40.193)", At(9, 0), At(18, 30), 46),
        new DemoPoi("admin", "Authorized Dealer — Antalya Muratpaşa", "Authorized Dealer",
            "POINT(30.717 36.886)", At(9, 0), At(19, 0), 42),
        new DemoPoi("poi.op", "Authorized Dealer — Konya Selçuklu", "Authorized Dealer",
            "POINT(32.492 37.897)", At(9, 0), At(18, 0), 39),

        new DemoPoi("admin", "Service Center — İstanbul Maslak", "Service Center",
            "POINT(29.020 41.108)", At(8, 30), At(20, 0), 64),
        new DemoPoi("admin", "Service Center — Ankara Çankaya", "Service Center",
            "POINT(32.862 39.900)", At(8, 30), At(20, 0), 49),
        new DemoPoi("poi.op", "Service Center — İzmir Bornova", "Service Center",
            "POINT(27.219 38.468)", At(8, 30), At(19, 30), 37),
        new DemoPoi("admin", "Service Center — Adana Seyhan", "Service Center",
            "POINT(35.328 36.995)", At(9, 0), At(18, 0), 30),
        new DemoPoi("poi.op", "Service Center — Gaziantep Şahinbey", "Service Center",
            "POINT(37.379 37.058)", At(9, 0), At(18, 0), 25),

        new DemoPoi("admin", "Drop-off Point — Kocaeli İzmit", "Drop-off Point",
            "POINT(29.923 40.765)", At(9, 0), At(18, 0), 34),
        new DemoPoi("poi.op", "Drop-off Point — Eskişehir Odunpazarı", "Drop-off Point",
            "POINT(30.522 39.766)", At(9, 0), At(18, 0), 28),
        new DemoPoi("admin", "Drop-off Point — Trabzon Ortahisar", "Drop-off Point",
            "POINT(39.720 41.002)", At(9, 0), At(17, 30), 23),
        new DemoPoi("poi.op", "Drop-off Point — Samsun İlkadım", "Drop-off Point",
            "POINT(36.331 41.287)", At(9, 0), At(17, 30), 19),

        // The 24/7 warehouse — the one row that proves the hours are real data, not a fixed label.
        new DemoPoi("admin", "Regional Warehouse — İstanbul Hadımköy", "Regional Warehouse",
            "POINT(28.660 41.115)", At(0, 0), At(23, 59), 68),
        new DemoPoi("admin", "Regional Warehouse — Ankara Sincan", "Regional Warehouse",
            "POINT(32.580 39.965)", At(7, 0), At(23, 0), 16),
        new DemoPoi("admin", "Regional Warehouse — İzmir Kemalpaşa", "Regional Warehouse",
            "POINT(27.420 38.425)", At(7, 0), At(23, 0), 13),
    };
}
