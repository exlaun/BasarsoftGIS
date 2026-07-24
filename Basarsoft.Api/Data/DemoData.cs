using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.Converters;

namespace Basarsoft.Api.Data;

/// <summary>
/// Deterministic, offline demo-data manifest. Large spatial fixtures are committed under
/// Data/demo instead of being embedded in source, so their OSM ids, capture date and derivation
/// remain reviewable. Nothing in this class performs a network request.
/// </summary>
public static class DemoData
{
    public const string Password = "secret123";
    public const string FixtureSnapshotDate = "2026-07-22";

    public const int ExpectedUserCount = 19;
    public const int ExpectedShapeCount = 328;
    public const int ExpectedPointCount = 218;
    public const int ExpectedLineCount = 60;
    public const int ExpectedPolygonCount = 50;
    public const int ExpectedPoiCount = 324;
    public const int ExpectedCategoryCount = 42;
    public const int ExpectedAreaCount = 17;
    public const int ExpectedProvinceCount = 81;
    public const int ExpectedRouteCount = 30;
    public const int ExpectedStopCount = 215;

    public const string RegionalManagerRoleName = "Regional Manager";
    public const string EditorRoleName = "Editor";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly WKTWriter WktWriter = new();

    public sealed record DemoProvince(
        string Name,
        string Region,
        string Color,
        string CapitalName,
        double CapitalLongitude,
        double CapitalLatitude,
        string SourceKey,
        string SourceId,
        string BoundarySourceId,
        string CapturedAt,
        string GeometrySource);

    public static readonly IReadOnlyList<DemoProvince> Provinces = LoadProvinces();

    public sealed record DemoUser(
        string Username,
        string Role,
        string[] DirectPermissions,
        string[]? AreaProvinceNames,
        string Demonstrates);

    // Order is a public demo contract. Existing ids stay 1..17; the two new operators append.
    public static readonly IReadOnlyList<DemoUser> Users =
    [
        new("admin", SeedData.AdminRoleName, [], null,
            "Full permissions and a private 100-shape nationwide inventory"),

        new("marmara_manager", RegionalManagerRoleName, [], null,
            "Marmara boundary inherited from the Regional Manager role"),
        new("aegean_manager", RegionalManagerRoleName, [], ProvinceNames("Aegean"),
            "Aegean user area overrides the role's Marmara default"),
        new("mediterranean_manager", RegionalManagerRoleName, [], ProvinceNames("Mediterranean"),
            "Mediterranean province-union authorization"),
        new("central_manager", RegionalManagerRoleName, [], ProvinceNames("Central Anatolia"),
            "Central Anatolia province-union authorization"),
        new("blacksea_manager", RegionalManagerRoleName, [], ProvinceNames("Black Sea"),
            "Black Sea province-union authorization"),
        new("eastern_manager", RegionalManagerRoleName, [], ProvinceNames("Eastern Anatolia"),
            "Eastern Anatolia province-union authorization"),
        new("southeast_manager", RegionalManagerRoleName, [], ProvinceNames("Southeastern Anatolia"),
            "Southeastern Anatolia province-union authorization"),

        new("ankara_editor", EditorRoleName, ["add_polygon"], ["Ankara"],
            "Point, line and direct-grant polygon scenarios inside Ankara"),
        new("istanbul_editor", EditorRoleName, [], ["İstanbul"],
            "Point and line scenarios inside İstanbul"),
        new("izmir_editor", EditorRoleName, [], ["İzmir"],
            "Point and line scenarios inside İzmir"),
        new("antalya_editor", EditorRoleName, [], ["Antalya"],
            "Point and line scenarios inside Antalya"),

        new("istanbul_operator", SeedData.OperatorRoleName, [], ["İstanbul"],
            "Five road-based IETT route examples inside İstanbul"),
        new("antalya_operator", SeedData.OperatorRoleName, [], ["Antalya"],
            "Five road-based municipal route examples inside Antalya"),
        new("gaziantep_operator", SeedData.OperatorRoleName, [], ["Gaziantep"],
            "One road-based Gaziulaş route example"),
        new("trabzon_operator", SeedData.OperatorRoleName, [], ["Trabzon"],
            "One road-based Trabzon route example"),

        new("viewer", SeedData.ViewerRoleName, [], null,
            "Read-only access with 25 immutable planning examples"),
        new("ankara_operator", SeedData.OperatorRoleName, [], ["Ankara"],
            "Five road-based EGO route examples inside Ankara"),
        new("izmir_operator", SeedData.OperatorRoleName, [], ["İzmir"],
            "Five road-based ESHOT route examples inside İzmir"),
    ];

    public sealed record DemoRole(
        string Name,
        string Description,
        string[] Permissions,
        string[]? AreaProvinceNames);

    public static readonly IReadOnlyList<DemoRole> Roles =
    [
        new(RegionalManagerRoleName,
            "Creates and manages a private inventory within an assigned province union",
            ["add_point", "add_line", "add_polygon"],
            ProvinceNames("Marmara")),
        new(EditorRoleName, "Creates and manages private points and lines",
            ["add_point", "add_line"], null),
    ];

    public sealed record DemoTheme(string Key, string Label, string Color);

    public static readonly IReadOnlyList<DemoTheme> Themes =
    [
        new("mobility", "Mobility & logistics", "#2563EB"),
        new("emergency", "Emergency & resilience", "#DC2626"),
        new("tourism", "Tourism & heritage", "#7C3AED"),
        new("environment", "Environment & recreation", "#0F766E"),
        new("municipal", "Municipal services", "#EA580C"),
    ];

    public sealed record DemoShape(
        string Owner,
        string Type,
        string Name,
        string Color,
        string Wkt,
        int DaysAgo,
        string Theme,
        string ScenarioId,
        string? FeatureKey,
        string[] RelatedFeatureKeys,
        string SourceKey,
        string SourceId,
        string SourceName,
        string CapturedAt,
        string GeometrySource);

    public static readonly IReadOnlyList<DemoShape> Shapes = LoadShapes();

    public sealed record DemoCategory(
        string Name,
        string? Parent,
        string? Color = null,
        string? IconKey = null);

    public static readonly IReadOnlyList<DemoCategory> Categories =
    [
        new("Food & Drink", null, "#f97316", "food"),
        new("Restaurant", "Food & Drink"),
        new("Cafe", "Food & Drink", IconKey: "coffee"),
        new("Bakery", "Food & Drink", IconKey: "bakery"),
        new("Fast Food", "Food & Drink"),

        new("Health", null, "#dc2626", "health"),
        new("Hospital", "Health"),
        new("Pharmacy", "Health", IconKey: "pharmacy"),
        new("24/7 Pharmacy", "Health", IconKey: "pharmacy"),

        new("Shopping", null, "#2563eb", "shopping"),
        new("Mall", "Shopping"),
        new("Supermarket", "Shopping"),

        new("Culture & Tourism", null, "#7c3aed", "culture"),
        new("Museum", "Culture & Tourism", IconKey: "museum"),
        new("Historical Site", "Culture & Tourism"),
        new("Hotel", "Culture & Tourism", IconKey: "hotel"),
        new("Visitor Center", "Culture & Tourism"),
        new("Art Gallery", "Culture & Tourism"),

        new("Services", null, "#16a34a", "services"),
        new("Bank", "Services", IconKey: "bank"),
        new("Gas Station", "Services", IconKey: "fuel"),
        new("Post Office", "Services", IconKey: "mail"),
        new("Municipality", "Services", IconKey: "government"),

        new("Transport", null, "#4f46e5", "transport"),
        new("Airport", "Transport", IconKey: "airport"),
        new("Train Station", "Transport"),
        new("Bus Terminal", "Transport"),
        new("Ferry Terminal", "Transport"),
        new("Metro Station", "Transport"),

        new("Education", null, "#0891b2", "education"),
        new("University", "Education"),
        new("Library", "Education"),
        new("High School", "Education"),

        new("Nature & Recreation", null, "#16a34a", "nature"),
        new("National Park", "Nature & Recreation"),
        new("Beach", "Nature & Recreation"),
        new("Park", "Nature & Recreation"),
        new("Botanical Garden", "Nature & Recreation"),

        new("Sports", null, "#f59e0b", "sports"),
        new("Stadium", "Sports"),
        new("Ski Center", "Sports"),
        new("Gym", "Sports"),
    ];

    public sealed record DemoPoi(
        string Owner,
        string Name,
        string Category,
        string Province,
        string Wkt,
        TimeOnly? Open,
        TimeOnly? Close,
        int DaysAgo,
        string SourceKey,
        string SourceId,
        string CapturedAt,
        string GeometrySource);

    // The fixture intentionally remains offline. When OSM did not provide a trustworthy simple
    // daily interval, use a deterministic category-based demo schedule instead of leaving the POI
    // blank. These are plausible presentation values, not claims about current real-world hours.
    public sealed record DemoOperatingHours(TimeOnly Open, TimeOnly Close);

    public static readonly IReadOnlyDictionary<string, DemoOperatingHours> PoiHoursByCategory =
        new Dictionary<string, DemoOperatingHours>(StringComparer.Ordinal)
        {
            ["Hospital"] = new(new TimeOnly(0, 0), new TimeOnly(23, 59)),
            ["Pharmacy"] = new(new TimeOnly(8, 30), new TimeOnly(19, 0)),
            ["24/7 Pharmacy"] = new(new TimeOnly(0, 0), new TimeOnly(23, 59)),
            ["Restaurant"] = new(new TimeOnly(10, 0), new TimeOnly(23, 0)),
            ["Cafe"] = new(new TimeOnly(8, 0), new TimeOnly(23, 0)),
            ["Bakery"] = new(new TimeOnly(6, 0), new TimeOnly(21, 0)),
            ["Fast Food"] = new(new TimeOnly(10, 0), new TimeOnly(23, 59)),
            ["Mall"] = new(new TimeOnly(10, 0), new TimeOnly(22, 0)),
            ["Supermarket"] = new(new TimeOnly(8, 0), new TimeOnly(22, 0)),
            ["Hotel"] = new(new TimeOnly(0, 0), new TimeOnly(23, 59)),
            ["Historical Site"] = new(new TimeOnly(9, 0), new TimeOnly(18, 0)),
            ["Museum"] = new(new TimeOnly(9, 0), new TimeOnly(18, 0)),
            ["Art Gallery"] = new(new TimeOnly(10, 0), new TimeOnly(19, 0)),
            ["Visitor Center"] = new(new TimeOnly(9, 0), new TimeOnly(18, 0)),
            ["Bank"] = new(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            ["Gas Station"] = new(new TimeOnly(0, 0), new TimeOnly(23, 59)),
            ["Post Office"] = new(new TimeOnly(8, 30), new TimeOnly(17, 30)),
            ["Municipality"] = new(new TimeOnly(8, 30), new TimeOnly(17, 30)),
            ["Airport"] = new(new TimeOnly(0, 0), new TimeOnly(23, 59)),
            ["Train Station"] = new(new TimeOnly(0, 0), new TimeOnly(23, 59)),
            ["Bus Terminal"] = new(new TimeOnly(0, 0), new TimeOnly(23, 59)),
            ["Ferry Terminal"] = new(new TimeOnly(5, 30), new TimeOnly(23, 30)),
            ["Metro Station"] = new(new TimeOnly(6, 0), new TimeOnly(23, 59)),
            ["University"] = new(new TimeOnly(8, 0), new TimeOnly(20, 0)),
            ["Library"] = new(new TimeOnly(9, 0), new TimeOnly(20, 0)),
            ["High School"] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            ["National Park"] = new(new TimeOnly(6, 0), new TimeOnly(20, 0)),
            ["Beach"] = new(new TimeOnly(6, 0), new TimeOnly(20, 0)),
            ["Park"] = new(new TimeOnly(6, 0), new TimeOnly(23, 0)),
            ["Botanical Garden"] = new(new TimeOnly(8, 0), new TimeOnly(20, 0)),
            ["Stadium"] = new(new TimeOnly(8, 0), new TimeOnly(22, 0)),
            ["Ski Center"] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            ["Gym"] = new(new TimeOnly(6, 0), new TimeOnly(23, 0)),
        };

    public static readonly IReadOnlyList<DemoPoi> Pois = LoadPois();

    public sealed record DemoRoute(
        string Owner,
        string Name,
        string City,
        string LineCode,
        string Kind,
        string Color,
        int DaysAgo,
        string GeometryWkt,
        double DistanceMeters,
        double DurationSeconds,
        string SourceKey,
        string SourceId,
        string SourceUrl,
        string CapturedAt,
        string GeometrySource);

    public sealed record DemoStop(
        string Route,
        string Name,
        string Wkt,
        int DaysAgo,
        string SourceKey,
        string SourceId,
        string SourceUrl,
        string CapturedAt,
        string GeometrySource);

    private static readonly RouteManifest RouteFixture = LoadRoutes();
    public static readonly IReadOnlyList<DemoRoute> Routes = RouteFixture.Routes;
    public static readonly IReadOnlyList<DemoStop> Stops = RouteFixture.Stops;

    private static string[] ProvinceNames(string region) =>
        Provinces.Where(province => province.Region == region)
            .Select(province => province.Name)
            .ToArray();

    private static IReadOnlyList<DemoProvince> LoadProvinces()
    {
        using var document = ReadDocument("Data", "provinces.geojson");
        return document.RootElement.GetProperty("features").EnumerateArray()
            .Select(feature =>
            {
                var properties = feature.GetProperty("properties");
                return new DemoProvince(
                    RequiredString(properties, "name"),
                    RequiredString(properties, "region"),
                    RequiredString(properties, "color"),
                    RequiredString(properties, "capitalName"),
                    properties.GetProperty("capitalLongitude").GetDouble(),
                    properties.GetProperty("capitalLatitude").GetDouble(),
                    RequiredString(properties, "sourceKey"),
                    RequiredString(properties, "sourceId"),
                    RequiredString(properties, "boundarySourceId"),
                    RequiredString(properties, "capturedAt"),
                    RequiredString(properties, "geometrySource"));
            })
            .ToArray();
    }

    private static IReadOnlyList<DemoShape> LoadShapes()
    {
        using var document = ReadDocument("Data", "demo", "shapes.geojson");
        return document.RootElement.GetProperty("features").EnumerateArray()
            .Select(feature =>
            {
                var properties = feature.GetProperty("properties");
                return new DemoShape(
                    RequiredString(properties, "owner"),
                    RequiredString(properties, "type"),
                    RequiredString(properties, "name"),
                    RequiredString(properties, "color"),
                    GeometryWkt(feature),
                    properties.GetProperty("daysAgo").GetInt32(),
                    RequiredString(properties, "theme"),
                    RequiredString(properties, "scenarioId"),
                    OptionalString(properties, "featureKey"),
                    properties.GetProperty("relatedFeatureKeys").EnumerateArray()
                        .Select(value => value.GetString()!).ToArray(),
                    RequiredString(properties, "sourceKey"),
                    RequiredString(properties, "sourceId"),
                    RequiredString(properties, "sourceName"),
                    RequiredString(properties, "capturedAt"),
                    RequiredString(properties, "geometrySource"));
            })
            .ToArray();
    }

    private static IReadOnlyList<DemoPoi> LoadPois()
    {
        using var document = ReadDocument("Data", "demo", "pois.geojson");
        return document.RootElement.GetProperty("features").EnumerateArray()
            .Select(feature =>
            {
                var properties = feature.GetProperty("properties");
                return new DemoPoi(
                    RequiredString(properties, "owner"),
                    RequiredString(properties, "name"),
                    RequiredString(properties, "category"),
                    RequiredString(properties, "province"),
                    GeometryWkt(feature),
                    OptionalTime(properties, "openTime"),
                    OptionalTime(properties, "closeTime"),
                    properties.GetProperty("daysAgo").GetInt32(),
                    RequiredString(properties, "sourceKey"),
                    RequiredString(properties, "sourceId"),
                    RequiredString(properties, "capturedAt"),
                    RequiredString(properties, "geometrySource"));
            })
            .ToArray();
    }

    private static RouteManifest LoadRoutes()
    {
        using var document = ReadDocument("Data", "demo", "routes.json");
        var snapshot = RequiredString(document.RootElement, "sourceSnapshot");
        if (!string.Equals(snapshot, FixtureSnapshotDate, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"routes.json sourceSnapshot must be {FixtureSnapshotDate}, found {snapshot}.");
        }
        var routes = new List<DemoRoute>();
        var stops = new List<DemoStop>();
        foreach (var item in document.RootElement.GetProperty("routes").EnumerateArray())
        {
            var name = RequiredString(item, "name");
            var daysAgo = item.GetProperty("daysAgo").GetInt32();
            routes.Add(new DemoRoute(
                RequiredString(item, "owner"),
                name,
                RequiredString(item, "city"),
                RequiredString(item, "lineCode"),
                RequiredString(item, "kind"),
                RequiredString(item, "color"),
                daysAgo,
                WktWriter.Write(JsonSerializer.Deserialize<Geometry>(
                    item.GetProperty("geometry").GetRawText(), JsonOptions)!),
                item.GetProperty("distanceMeters").GetDouble(),
                item.GetProperty("durationSeconds").GetDouble(),
                RequiredString(item, "sourceKey"),
                RequiredString(item, "sourceId"),
                RequiredString(item, "sourceUrl"),
                RequiredString(item, "capturedAt"),
                RequiredString(item, "geometrySource")));

            foreach (var stop in item.GetProperty("stops").EnumerateArray())
            {
                var point = new Point(
                    stop.GetProperty("longitude").GetDouble(),
                    stop.GetProperty("latitude").GetDouble()) { SRID = 4326 };
                stops.Add(new DemoStop(
                    name,
                    RequiredString(stop, "name"),
                    WktWriter.Write(point),
                    daysAgo,
                    RequiredString(stop, "sourceKey"),
                    RequiredString(stop, "sourceId"),
                    RequiredString(stop, "sourceUrl"),
                    RequiredString(stop, "capturedAt"),
                    RequiredString(stop, "geometrySource")));
            }
        }
        return new RouteManifest(routes, stops);
    }

    private static string GeometryWkt(JsonElement feature)
    {
        var geometry = JsonSerializer.Deserialize<Geometry>(
            feature.GetProperty("geometry").GetRawText(), JsonOptions)
            ?? throw new InvalidOperationException("Demo GeoJSON feature has no geometry.");
        geometry.SRID = 4326;
        return WktWriter.Write(geometry);
    }

    private static JsonDocument ReadDocument(params string[] relativeParts)
    {
        var path = Path.Combine([AppContext.BaseDirectory, .. relativeParts]);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Required demo fixture is missing: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string RequiredString(JsonElement element, string property)
    {
        var value = element.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Demo fixture property '{property}' is required.")
            : value;
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static TimeOnly? OptionalTime(JsonElement element, string property) =>
        OptionalString(element, property) is { } value
            ? TimeOnly.Parse(value)
            : null;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new GeoJsonConverterFactory());
        return options;
    }

    private sealed record RouteManifest(
        IReadOnlyList<DemoRoute> Routes,
        IReadOnlyList<DemoStop> Stops);
}
