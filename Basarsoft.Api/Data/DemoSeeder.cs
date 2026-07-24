using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Basarsoft.Api.Models;
using Basarsoft.Api.Services;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.IO;
using NetTopologySuite.IO.Converters;
using BC = BCrypt.Net.BCrypt;

namespace Basarsoft.Api.Data;

/// <summary>
/// Destructively rebuilds the deterministic, source-backed Turkey demonstration dataset.
/// The committed fixtures are validated completely before the transaction begins and are never
/// refreshed from the network during seeding.
/// </summary>
public static class DemoSeeder
{
    private static readonly string[] Tables =
    [
        "user_permissions", "role_permissions", "user_roles",
        "tbl_geo_authorization", "tbl_poi", "tbl_poi_category",
        "tbl_location_analysis_criterion", "tbl_location_analysis",
        "tbl_stop", "tbl_route",
        "tbl_point", "tbl_line", "tbl_polygon",
        "permissions", "roles", "users",
    ];

    private static readonly string[] Sequences =
    [
        "seq_users", "seq_roles", "seq_permissions",
        "seq_user_roles", "seq_role_permissions", "seq_user_permissions",
        "seq_tbl_point", "seq_tbl_line", "seq_tbl_polygon",
        "seq_tbl_geo_authorization", "seq_tbl_poi_category", "seq_tbl_poi",
        "seq_tbl_location_analysis", "seq_tbl_location_analysis_criterion",
        "seq_tbl_route", "seq_tbl_stop",
    ];

    private const int Srid = 4326;
    private const int ProvincePoiBaselineCount = DemoData.ExpectedProvinceCount * 3;
    private static readonly WKTReader Reader = new();
    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Regex OsmObjectId = new(
        "^(node|way|relation)/[1-9][0-9]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WeakHospitalName = new(
        @"\beski\b|aile sagligi|saglik ocagi|saglik merkezi|saglik mudurlugu|\bosgb\b|"
        + @"ortak saglik|\bgiris\b|\bentrance\b|diyaliz|\b112\b|acil servis|"
        + @"dis hekimligi fakultesi|agiz ve dis sagligi merkezi|tip merkezi|\bcagem\b|\bunitesi\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WeakMallName = new(
        @"^market$|bilgisayar|\bkolej\b|\bokul\b|kafeterya|\bevent\b|organizasyon|"
        + @"yapi market|konfeksiyon.*mobilya|\bevkur\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WeakRestaurantName = new(
        @"^lokanta$|(?:^|\s)(?:cafe|kafe)$|\bbufe\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<bool> RunAsync(
        AppDbContext db, IHostEnvironment env, ILogger logger, bool skipConfirmation)
    {
        var database = db.Database.GetDbConnection().Database;
        if (!env.IsDevelopment())
        {
            logger.LogError(
                "Refusing to seed demo data outside Development (current environment: {Environment}).",
                env.EnvironmentName);
            return false;
        }

        try
        {
            ValidateManifest();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Demo seed preflight FAILED. Nothing was changed.");
            return false;
        }

        logger.LogWarning(
            "This DELETES ALL DATA in database '{Database}' and replaces it with the demo dataset.",
            database);
        await LogCurrentContentsAsync(db, logger);
        if (!skipConfirmation && !Confirm(database))
        {
            logger.LogInformation("Aborted. Nothing was changed.");
            return false;
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await WipeAsync(db);
            await AdminSeeder.SeedAsync(db);
            await ProvinceSeeder.SeedAsync(db);

            var permissions = await db.Permissions.ToDictionaryAsync(permission => permission.Name, permission => permission.Id);
            var users = await InsertUsersAsync(db);
            var roles = await InsertRolesAsync(db);
            await InsertGrantsAsync(db, users, roles, permissions);
            await InsertAreasAsync(db, users, roles);
            var categories = await InsertCategoriesAsync(db, users["admin"]);
            await InsertShapesAsync(db, users);
            await InsertPoisAsync(db, users, categories);
            await InsertTransportationAsync(db, users);

            await BackdateModifiedDatesAsync(db);
            db.ChangeTracker.Clear();
            await AssertEveryFeatureIsInsideItsAreaAsync(db);
            await AssertDemoContractAsync(db, users);
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Demo seed FAILED. The database was rolled back to its previous contents.");
            return false;
        }

        try
        {
            await LogSummaryAsync(db, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Demo seed committed successfully, but the post-commit summary could not be loaded.");
        }
        return true;
    }

    /// <summary>Runs the same complete fixture preflight as seed-demo without touching a database.</summary>
    public static void ValidateManifest()
    {
        var expectedUsers = new[]
        {
            "admin",
            "marmara_manager", "aegean_manager", "mediterranean_manager", "central_manager",
            "blacksea_manager", "eastern_manager", "southeast_manager",
            "ankara_editor", "istanbul_editor", "izmir_editor", "antalya_editor",
            "istanbul_operator", "antalya_operator", "gaziantep_operator", "trabzon_operator",
            "viewer", "ankara_operator", "izmir_operator",
        };
        Require(DemoData.Password == "secret123", "All demo accounts must use password 'secret123'.");
        Require(DemoData.Users.Count == DemoData.ExpectedUserCount,
            $"Expected {DemoData.ExpectedUserCount} users, found {DemoData.Users.Count}.");
        Require(DemoData.Users.Select(user => user.Username).SequenceEqual(expectedUsers),
            "Demo account order changed; ids 1..19 are part of the demo contract.");
        RequireUnique(DemoData.Users.Select(user => user.Username), "usernames");

        var permissionNames = SeedData.Permissions.Select(permission => permission.Name)
            .ToHashSet(StringComparer.Ordinal);
        var rolePermissions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [SeedData.AdminRoleName] = new(permissionNames, StringComparer.Ordinal),
            [SeedData.OperatorRoleName] = SeedData.OperatorPermissions.ToHashSet(StringComparer.Ordinal),
            [SeedData.ViewerRoleName] = [],
        };
        foreach (var role in DemoData.Roles)
        {
            RequireUnique(role.Permissions, $"permissions on role '{role.Name}'");
            Require(role.Permissions.All(permissionNames.Contains),
                $"Role '{role.Name}' refers to an unknown permission.");
            rolePermissions[role.Name] = role.Permissions.ToHashSet(StringComparer.Ordinal);
        }
        Require(rolePermissions.Count == 5, "Demo permission matrix must have exactly five roles.");
        Require(rolePermissions[SeedData.OperatorRoleName].SetEquals(["manage_transport"]),
            "Operator must inherit manage_transport only.");

        var boundaries = LoadProvinceBoundaries();
        Require(boundaries.Count == DemoData.ExpectedProvinceCount,
            $"Expected {DemoData.ExpectedProvinceCount} province boundaries.");
        Require(DemoData.Provinces.Count == DemoData.ExpectedProvinceCount,
            $"Expected {DemoData.ExpectedProvinceCount} province catalogue rows.");
        RequireUnique(DemoData.Provinces.Select(province => province.Name), "province names");
        RequireUnique(DemoData.Provinces.Select(province => $"{province.SourceKey}:{province.SourceId}"),
            "province capital source identities");
        RequireUnique(DemoData.Provinces.Select(province => province.BoundarySourceId),
            "province boundary source identities");
        foreach (var province in DemoData.Provinces)
        {
            Require(boundaries.ContainsKey(province.Name), $"Province source is missing '{province.Name}'.");
            Require(HexColor.IsMatch(province.Color), $"Province '{province.Name}' has invalid color.");
            RequireSource(province.SourceKey, province.SourceId, province.CapturedAt, province.GeometrySource,
                $"province '{province.Name}'");
            Require(province.BoundarySourceId.StartsWith("relation/", StringComparison.Ordinal),
                $"Province '{province.Name}' has invalid boundary source identity.");
            var capital = new Point(province.CapitalLongitude, province.CapitalLatitude) { SRID = Srid };
            Require(boundaries[province.Name].Covers(capital),
                $"Capital '{province.CapitalName}' is outside province '{province.Name}'.");
        }
        var expectedRegions = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Marmara"] = 11, ["Aegean"] = 8, ["Mediterranean"] = 8,
            ["Central Anatolia"] = 13, ["Black Sea"] = 18,
            ["Eastern Anatolia"] = 14, ["Southeastern Anatolia"] = 9,
        };
        RequireDictionaryEqual(expectedRegions, DemoData.Provinces.GroupBy(province => province.Region)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            "province-to-region distribution");

        foreach (var role in DemoData.Roles)
            ValidateAreaScope(role.AreaProvinceNames, boundaries, $"role '{role.Name}'");
        foreach (var user in DemoData.Users)
        {
            Require(rolePermissions.TryGetValue(user.Role, out var inherited),
                $"User '{user.Username}' refers to unknown role '{user.Role}'.");
            RequireUnique(user.DirectPermissions, $"direct permissions on '{user.Username}'");
            Require(user.DirectPermissions.All(permissionNames.Contains),
                $"User '{user.Username}' refers to an unknown direct permission.");
            Require(!user.DirectPermissions.Any(inherited!.Contains),
                $"User '{user.Username}' duplicates an inherited permission.");
            ValidateAreaScope(user.AreaProvinceNames, boundaries, $"user '{user.Username}'");
        }
        Require(DemoData.Users.Count(user => user.AreaProvinceNames is not null)
                + DemoData.Roles.Count(role => role.AreaProvinceNames is not null)
                == DemoData.ExpectedAreaCount,
            $"Expected {DemoData.ExpectedAreaCount} authorization areas.");

        ValidateShapes(boundaries, rolePermissions);
        ValidateCategoriesAndPois(boundaries);
        ValidateTransportation(boundaries, rolePermissions);
    }

    public static Dictionary<string, int> ExpectedPoiCategoryUse() =>
        DemoData.Pois.GroupBy(poi => poi.Category)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static void ValidateShapes(
        IReadOnlyDictionary<string, Geometry> boundaries,
        IReadOnlyDictionary<string, HashSet<string>> rolePermissions)
    {
        Require(DemoData.Shapes.Count == DemoData.ExpectedShapeCount,
            $"Expected {DemoData.ExpectedShapeCount} private shapes.");
        Require(DemoData.Shapes.Count(shape => shape.Type == "point") == DemoData.ExpectedPointCount
                && DemoData.Shapes.Count(shape => shape.Type == "line") == DemoData.ExpectedLineCount
                && DemoData.Shapes.Count(shape => shape.Type == "polygon") == DemoData.ExpectedPolygonCount,
            "Shape type distribution must be 218 points, 60 lines and 50 polygons.");
        RequireUnique(DemoData.Shapes.Select(shape => shape.Name), "shape names");
        RequireUnique(DemoData.Shapes.Select(shape => $"{shape.SourceKey}:{shape.SourceId}"),
            "shape source identities");

        var expectedOwners = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["admin"] = 100,
            ["marmara_manager"] = 21, ["aegean_manager"] = 21,
            ["mediterranean_manager"] = 21, ["central_manager"] = 21,
            ["blacksea_manager"] = 21, ["eastern_manager"] = 21,
            ["southeast_manager"] = 21,
            ["ankara_editor"] = 20,
            ["istanbul_editor"] = 12, ["izmir_editor"] = 12, ["antalya_editor"] = 12,
            ["viewer"] = 25,
        };
        RequireDictionaryEqual(expectedOwners, DemoData.Shapes.GroupBy(shape => shape.Owner)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            "shape owner distribution");

        var users = DemoData.Users.ToDictionary(user => user.Username, StringComparer.Ordinal);
        var themes = DemoData.Themes.ToDictionary(theme => theme.Key, StringComparer.Ordinal);
        var featureKeys = DemoData.Shapes.Where(shape => shape.FeatureKey is not null)
            .ToDictionary(shape => shape.FeatureKey!, StringComparer.Ordinal);
        var effectiveAreas = EffectiveManifestAreas(boundaries);
        foreach (var shape in DemoData.Shapes)
        {
            Require(users.TryGetValue(shape.Owner, out var owner),
                $"Shape '{shape.Name}' has unknown owner '{shape.Owner}'.");
            Require(themes.TryGetValue(shape.Theme, out var theme) && theme.Color == shape.Color,
                $"Shape '{shape.Name}' does not use its theme color.");
            Require(shape.Name.Length <= 80 && HexColor.IsMatch(shape.Color),
                $"Shape '{shape.Name}' has invalid presentation metadata.");
            RequireSource(shape.SourceKey, shape.SourceId, shape.CapturedAt, shape.GeometrySource,
                $"shape '{shape.Name}'");
            Require(!string.IsNullOrWhiteSpace(shape.SourceName),
                $"Shape '{shape.Name}' is missing its canonical source name.");
            var expectedType = shape.Type switch
            {
                "point" => OgcGeometryType.Point,
                "line" => OgcGeometryType.LineString,
                "polygon" => OgcGeometryType.Polygon,
                _ => throw new InvalidOperationException($"Unknown shape type '{shape.Type}'."),
            };
            var geometry = Parse(shape.Wkt, expectedType, shape.Name);
            if (owner!.Username != "viewer")
            {
                var effective = rolePermissions[owner.Role]
                    .Concat(owner.DirectPermissions).ToHashSet(StringComparer.Ordinal);
                Require(effective.Contains($"add_{shape.Type}"),
                    $"Owner '{owner.Username}' cannot manage seeded {shape.Type} '{shape.Name}'.");
            }
            if (effectiveAreas[shape.Owner] is { } area)
                Require(area.Covers(geometry),
                    $"Restricted shape '{shape.Name}' is outside '{shape.Owner}' area.");
        }

        foreach (var theme in DemoData.Themes)
        {
            Require(DemoData.Shapes.Count(shape => shape.Type == "line" && shape.Theme == theme.Key) == 12,
                $"Theme '{theme.Key}' must contain exactly 12 lines.");
            Require(DemoData.Shapes.Count(shape => shape.Type == "polygon" && shape.Theme == theme.Key) == 10,
                $"Theme '{theme.Key}' must contain exactly 10 polygons.");
        }
        foreach (var owner in DemoData.Users.Where(user =>
                     user.Role == DemoData.RegionalManagerRoleName
                     || user.Username.EndsWith("_editor", StringComparison.Ordinal)))
        {
            Require(DemoData.Shapes.Where(shape => shape.Owner == owner.Username)
                    .Select(shape => shape.Theme).Distinct(StringComparer.Ordinal).Count() >= 3,
                $"Owner '{owner.Username}' must demonstrate at least three drawing themes.");
        }

        // Scenario drawings must remain distinct from reference/transport layers. Checking the
        // envelope first keeps this inexpensive even with detailed province relations while
        // EqualsTopologically catches reversed rings/lines and different starting vertices.
        var routeLines = DemoData.Routes
            .Select(route => Parse(route.GeometryWkt, OgcGeometryType.LineString, route.Name))
            .ToArray();
        foreach (var line in DemoData.Shapes.Where(shape => shape.Type == "line"))
        {
            var geometry = Parse(line.Wkt, OgcGeometryType.LineString, line.Name);
            Require(!routeLines.Any(route => IsTopologicalDuplicate(geometry, route)),
                $"Scenario line '{line.Name}' duplicates a transportation route.");
        }
        foreach (var polygon in DemoData.Shapes.Where(shape => shape.Type == "polygon"))
        {
            var geometry = Parse(polygon.Wkt, OgcGeometryType.Polygon, polygon.Name);
            Require(!boundaries.Values.Any(boundary => IsTopologicalDuplicate(geometry, boundary)),
                $"Scenario polygon '{polygon.Name}' duplicates a province boundary.");
        }

        foreach (var scenario in DemoData.Shapes.Where(shape => shape.Type is "line" or "polygon"))
        {
            var geometry = Parse(scenario.Wkt,
                scenario.Type == "line" ? OgcGeometryType.LineString : OgcGeometryType.Polygon,
                scenario.Name);
            var related = scenario.RelatedFeatureKeys.Select(key =>
                featureKeys.TryGetValue(key, out var point)
                    ? point
                    : throw new InvalidOperationException(
                        $"Scenario '{scenario.ScenarioId}' refers to missing point '{key}'."))
                .ToArray();
            Require(related.All(point => point.Theme == scenario.Theme && point.Color == scenario.Color),
                $"Scenario '{scenario.ScenarioId}' mixes theme presentation.");
            if (scenario.Type == "line")
            {
                Require(related.Length == 2, $"Line scenario '{scenario.ScenarioId}' needs two anchors.");
                Require(related.All(point => DistanceToLineMeters(
                        (Point)Parse(point.Wkt, OgcGeometryType.Point, point.Name),
                        (LineString)geometry) <= 100),
                    $"Line scenario '{scenario.ScenarioId}' has an anchor more than 100m away.");
            }
            else
            {
                Require(related.Length == 1 && geometry.Contains(Parse(
                    related[0].Wkt, OgcGeometryType.Point, related[0].Name)),
                    $"Polygon scenario '{scenario.ScenarioId}' must contain its site point.");
            }
        }
    }

    private static void ValidateCategoriesAndPois(IReadOnlyDictionary<string, Geometry> boundaries)
    {
        Require(DemoData.Categories.Count == DemoData.ExpectedCategoryCount,
            $"Expected {DemoData.ExpectedCategoryCount} POI categories.");
        RequireUnique(DemoData.Categories.Select(category => category.Name), "category names");
        var categories = DemoData.Categories.ToDictionary(category => category.Name, StringComparer.Ordinal);
        foreach (var category in DemoData.Categories)
        {
            Require(category.Parent is null || categories.ContainsKey(category.Parent),
                $"Category '{category.Name}' has unknown parent '{category.Parent}'.");
            Require(PoiIconCatalog.TryNormalize(category.IconKey, out var normalized)
                    && normalized == category.IconKey,
                $"Category '{category.Name}' has invalid icon '{category.IconKey}'.");
        }
        foreach (var category in DemoData.Categories)
        {
            var chain = new HashSet<string>(StringComparer.Ordinal);
            var current = category;
            while (true)
            {
                Require(chain.Add(current.Name),
                    $"Category parent chain for '{category.Name}' contains a cycle.");
                if (current.Parent is null)
                    break;
                Require(categories.TryGetValue(current.Parent, out var parent),
                    $"Category '{category.Name}' cannot reach a root category.");
                current = parent!;
            }
        }
        var parentNames = DemoData.Categories.Where(category => category.Parent is not null)
            .Select(category => category.Parent!).ToHashSet(StringComparer.Ordinal);
        var leaves = DemoData.Categories.Where(category => !parentNames.Contains(category.Name))
            .Select(category => category.Name).ToHashSet(StringComparer.Ordinal);
        Require(DemoData.Categories.Count(category => category.Parent is null) == 9 && leaves.Count == 33,
            "Category tree must contain nine roots and 33 leaves.");

        Require(DemoData.Pois.Count == DemoData.ExpectedPoiCount,
            $"Expected {DemoData.ExpectedPoiCount} POIs.");
        RequireUnique(DemoData.Pois.Select(poi => $"{poi.SourceKey}:{poi.SourceId}"),
            "POI source identities");
        foreach (var poi in DemoData.Pois)
        {
            Require(poi.Owner == "admin", $"Shared POI '{poi.Name}' must be admin-owned.");
            Require(leaves.Contains(poi.Category), $"POI '{poi.Name}' must use a leaf category.");
            Require(poi.SourceKey == "openstreetmap" && OsmObjectId.IsMatch(poi.SourceId),
                $"POI '{poi.Name}' must retain its canonical OSM object identity.");
            Require(boundaries.TryGetValue(poi.Province, out var boundary),
                $"POI '{poi.Name}' refers to unknown province '{poi.Province}'.");
            Require(DemoData.PoiHoursByCategory.TryGetValue(poi.Category, out var expectedHours),
                $"POI category '{poi.Category}' has no logical demo operating-hours profile.");
            Require(poi.Open == expectedHours!.Open && poi.Close == expectedHours.Close,
                $"POI '{poi.Name}' does not use the '{poi.Category}' demo operating-hours profile.");
            Require(boundary!.Covers(Parse(poi.Wkt, OgcGeometryType.Point, poi.Name)),
                $"POI '{poi.Name}' is outside '{poi.Province}'.");
            RequireSource(poi.SourceKey, poi.SourceId, poi.CapturedAt, poi.GeometrySource,
                $"POI '{poi.Name}'");
        }

        var foodCategories = new HashSet<string>(
            ["Restaurant", "Cafe", "Bakery", "Fast Food"], StringComparer.Ordinal);
        var shoppingCategories = new HashSet<string>(
            ["Mall", "Supermarket"], StringComparer.Ordinal);
        var baselinePois = DemoData.Pois.Take(ProvincePoiBaselineCount).ToArray();
        var baselineByProvince = baselinePois.GroupBy(poi => poi.Province)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        Require(baselinePois.Length == ProvincePoiBaselineCount
                && baselineByProvince.Count == DemoData.ExpectedProvinceCount,
            "The first 243 POIs must be the nationwide province baseline.");
        foreach (var province in boundaries.Keys)
        {
            Require(baselineByProvince.TryGetValue(province, out var rows) && rows.Length == 3,
                $"Province '{province}' must have exactly three baseline POIs.");
            var baselineRows = rows!;
            Require(baselineRows.Count(poi => poi.Category == "Hospital") == 1
                    && baselineRows.Count(poi => foodCategories.Contains(poi.Category)) == 1
                    && baselineRows.Count(poi => shoppingCategories.Contains(poi.Category)) == 1,
                $"Province '{province}' baseline must contain one hospital, food venue and shopping venue.");

            var hospital = baselineRows.Single(poi => poi.Category == "Hospital");
            Require(!WeakHospitalName.IsMatch(FoldForSemanticValidation(hospital.Name)),
                $"Baseline hospital '{hospital.Name}' in '{province}' is semantically unsuitable.");
            var mall = baselineRows.SingleOrDefault(poi => poi.Category == "Mall");
            Require(mall is null || !WeakMallName.IsMatch(FoldForSemanticValidation(mall.Name)),
                $"Baseline mall '{mall?.Name}' in '{province}' is semantically unsuitable.");
            var restaurant = baselineRows.SingleOrDefault(poi => poi.Category == "Restaurant");
            Require(restaurant is null
                    || !WeakRestaurantName.IsMatch(FoldForSemanticValidation(restaurant.Name)),
                $"Baseline restaurant '{restaurant?.Name}' in '{province}' is semantically unsuitable.");
        }

        var provinceCounts = DemoData.Pois.GroupBy(poi => poi.Province)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var expectedExtras = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["İstanbul"] = 15,
            ["Ankara"] = 12,
            ["İzmir"] = 10,
            ["Antalya"] = 10,
            ["Bursa"] = 6,
            ["Adana"] = 5,
            ["Gaziantep"] = 5,
            ["Konya"] = 5,
            ["Kayseri"] = 4,
            ["Mersin"] = 4,
            ["Trabzon"] = 3,
            ["Eskişehir"] = 2,
        };
        foreach (var province in boundaries.Keys)
        {
            var expected = 3 + expectedExtras.GetValueOrDefault(province);
            Require(provinceCounts.GetValueOrDefault(province) == expected,
                $"Province '{province}' must contain exactly {expected} shared POIs.");
        }
        var provincePois = DemoData.Pois.GroupBy(poi => poi.Province)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var province in boundaries.Keys)
        {
            var rows = provincePois[province];
            Require(rows.Any(poi => poi.Category == "Hospital"),
                $"Province '{province}' must include a mapped hospital.");
            Require(rows.Any(poi => foodCategories.Contains(poi.Category)),
                $"Province '{province}' must include a mapped food venue.");
            Require(rows.Any(poi => poi.Category is "Mall" or "Supermarket"),
                $"Province '{province}' must include a mapped mall/supermarket.");
        }
        var categoryUse = ExpectedPoiCategoryUse();
        Require(leaves.All(leaf => categoryUse.GetValueOrDefault(leaf) >= 2),
            "Every POI leaf category must have at least two examples.");
    }

    private static string FoldForSemanticValidation(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var buffer = new char[decomposed.Length];
        var length = 0;
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            buffer[length++] = character == 'ı' ? 'i' : char.ToLowerInvariant(character);
        }
        return new string(buffer, 0, length);
    }

    private static void ValidateTransportation(
        IReadOnlyDictionary<string, Geometry> boundaries,
        IReadOnlyDictionary<string, HashSet<string>> rolePermissions)
    {
        Require(DemoData.Routes.Count == DemoData.ExpectedRouteCount,
            $"Expected {DemoData.ExpectedRouteCount} transportation routes.");
        Require(DemoData.Stops.Count == DemoData.ExpectedStopCount,
            $"Expected {DemoData.ExpectedStopCount} transportation stops.");
        RequireUnique(DemoData.Routes.Select(route => route.Name), "route names");
        RequireUnique(DemoData.Routes.Select(route => $"{route.SourceKey}:{route.SourceId}"),
            "route source identities");
        var users = DemoData.Users.ToDictionary(user => user.Username, StringComparer.Ordinal);
        var areas = EffectiveManifestAreas(boundaries);
        foreach (var route in DemoData.Routes)
        {
            Require(users.TryGetValue(route.Owner, out var owner),
                $"Route '{route.Name}' has unknown owner '{route.Owner}'.");
            Require(rolePermissions[owner!.Role].Concat(owner.DirectPermissions)
                    .Contains(SeedData.ManageTransportPermission),
                $"Owner '{route.Owner}' cannot manage route '{route.Name}'.");
            Require(HexColor.IsMatch(route.Color) && route.Name.Length <= 80,
                $"Route '{route.Name}' has invalid presentation metadata.");
            Require(route.DistanceMeters > 0 && route.DurationSeconds > 0,
                $"Route '{route.Name}' must carry persisted positive OSRM metrics.");
            Require(route.DaysAgo >= 0,
                $"Route '{route.Name}' has an invalid fixture age.");
            Require(Uri.TryCreate(route.SourceUrl, UriKind.Absolute, out var routeSourceUrl)
                    && routeSourceUrl.Scheme is "http" or "https",
                $"Route '{route.Name}' has an invalid source URL.");
            var line = Parse(route.GeometryWkt, OgcGeometryType.LineString, route.Name);
            if (areas[route.Owner] is { } area)
                Require(area.Covers(line), $"Route '{route.Name}' leaves '{route.Owner}' authorization.");
            RequireSource(route.SourceKey, route.SourceId, route.CapturedAt, route.GeometrySource,
                $"route '{route.Name}'");
        }
        foreach (var cityGroup in DemoData.Routes.GroupBy(route => route.City))
            Require(cityGroup.Select(route => route.Color).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    == cityGroup.Count(),
                $"Route colors must be distinct within '{cityGroup.Key}'.");

        var routes = DemoData.Routes.ToDictionary(route => route.Name, StringComparer.Ordinal);
        var stopGroups = DemoData.Stops.GroupBy(stop => stop.Route)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        Require(routes.Keys.All(stopGroups.ContainsKey), "Every route must have stops.");
        foreach (var (routeName, stops) in stopGroups)
        {
            Require(routes.TryGetValue(routeName, out var route),
                $"Stops refer to unknown route '{routeName}'.");
            RequireUnique(stops.Select(stop => stop.SourceId), $"stop source ids on '{routeName}'");
            RequireUnique(stops.Select(stop => stop.Name), $"stop names on '{routeName}'");
            Require(route!.Kind == "urban" ? stops.Count == 8 : stops.Count is >= 2 and <= 4,
                $"Route '{routeName}' has an invalid representative-stop count.");
            var line = Parse(route.GeometryWkt, OgcGeometryType.LineString, routeName);
            foreach (var stop in stops)
            {
                var point = Parse(stop.Wkt, OgcGeometryType.Point, stop.Name);
                Require(stop.Name.Length <= 80 && stop.DaysAgo >= 0,
                    $"Stop '{stop.Name}' has invalid presentation or fixture-age metadata.");
                Require(DistanceToLineMeters((Point)point, (LineString)line) <= 100,
                    $"Route '{routeName}' does not pass within 100m of stop '{stop.Name}'.");
                if (areas[route.Owner] is { } stopArea)
                    Require(stopArea.Covers(point),
                        $"Stop '{stop.Name}' leaves '{route.Owner}' authorization.");
                RequireSource(stop.SourceKey, stop.SourceId, stop.CapturedAt,
                    stop.GeometrySource, $"stop '{stop.Name}'");
                Require(Uri.TryCreate(stop.SourceUrl, UriKind.Absolute, out var sourceUrl)
                        && sourceUrl.Scheme is "http" or "https",
                    $"Stop '{stop.Name}' has an invalid source URL.");
            }
        }
        var expectedUrban = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["İstanbul"] = 5, ["Ankara"] = 5, ["İzmir"] = 5, ["Antalya"] = 5,
            ["Bursa"] = 1, ["Adana"] = 1, ["Konya"] = 1, ["Gaziantep"] = 1, ["Trabzon"] = 1,
        };
        RequireDictionaryEqual(expectedUrban, DemoData.Routes.Where(route => route.Kind == "urban")
            .GroupBy(route => route.City)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            "urban route distribution");
        var expectedUrbanInventory = new HashSet<string>(StringComparer.Ordinal)
        {
            "İstanbul|34BZ", "İstanbul|34AS", "İstanbul|500T", "İstanbul|15F", "İstanbul|25E",
            "Ankara|205", "Ankara|303", "Ankara|334-6", "Ankara|413", "Ankara|442",
            "İzmir|202", "İzmir|515", "İzmir|584", "İzmir|808", "İzmir|950",
            "Antalya|KL08", "Antalya|VS18", "Antalya|LC07A", "Antalya|ML22", "Antalya|VF63",
            "Bursa|38/B-2", "Adana|114", "Konya|4-A", "Gaziantep|B39", "Trabzon|121",
        };
        Require(expectedUrbanInventory.SetEquals(DemoData.Routes
                .Where(route => route.Kind == "urban")
                .Select(route => $"{route.City}|{route.LineCode}")),
            "Urban route city/code inventory differs from the approved manifest.");
        var expectedIntercityNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Intercity corridor · İstanbul–Ankara",
            "Intercity corridor · İstanbul–Bursa–İzmir",
            "Intercity corridor · Ankara–Konya–Antalya",
            "Intercity corridor · İzmir–Aydın–Muğla–Antalya",
            "Intercity corridor · Adana–Gaziantep–Şanlıurfa",
        };
        Require(expectedIntercityNames.SetEquals(DemoData.Routes
                .Where(route => route.Kind == "intercity")
                .Select(route => route.Name)),
            "Intercity corridor inventory differs from the approved manifest.");
    }

    private static Dictionary<string, Geometry?> EffectiveManifestAreas(
        IReadOnlyDictionary<string, Geometry> boundaries)
    {
        var roles = DemoData.Roles.ToDictionary(role => role.Name, StringComparer.Ordinal);
        return DemoData.Users.ToDictionary(
            user => user.Username,
            user =>
            {
                var names = user.AreaProvinceNames
                    ?? (roles.TryGetValue(user.Role, out var role) ? role.AreaProvinceNames : null);
                return names is null ? null : BuildArea(names, boundaries);
            },
            StringComparer.Ordinal);
    }

    private static void ValidateAreaScope(
        string[]? provinceNames,
        IReadOnlyDictionary<string, Geometry> boundaries,
        string what)
    {
        if (provinceNames is null) return;
        Require(provinceNames.Length > 0, $"Authorization area for {what} is empty.");
        RequireUnique(provinceNames, $"province names in {what} area");
        Require(provinceNames.All(boundaries.ContainsKey), $"Authorization area for {what} has an unknown province.");
        _ = BuildArea(provinceNames, boundaries);
    }

    private static Dictionary<string, Geometry> LoadProvinceBoundaries()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "provinces.geojson");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var options = new JsonSerializerOptions();
        options.Converters.Add(new GeoJsonConverterFactory());
        var result = new Dictionary<string, Geometry>(StringComparer.Ordinal);
        foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        {
            var name = feature.GetProperty("properties").GetProperty("name").GetString()
                ?? throw new InvalidOperationException("Province name is required.");
            var geometry = JsonSerializer.Deserialize<Geometry>(
                feature.GetProperty("geometry").GetRawText(), options)
                ?? throw new InvalidOperationException($"Province '{name}' has no geometry.");
            if (!geometry.IsValid) geometry = GeometryFixer.Fix(geometry);
            geometry.SRID = Srid;
            result.Add(name, geometry);
        }
        return result;
    }

    private static Geometry BuildArea(
        IEnumerable<string> provinceNames,
        IReadOnlyDictionary<string, Geometry> boundaries)
    {
        var geometries = provinceNames.Select(name => boundaries[name]).ToArray();
        var union = geometries.Skip(1).Aggregate(geometries[0].Copy(), (area, next) => area.Union(next));
        if (!union.IsValid) union = GeometryFixer.Fix(union);
        union.SRID = Srid;
        return NormalizeMultiPolygon(union);
    }

    private static MultiPolygon NormalizeMultiPolygon(Geometry geometry)
    {
        if (geometry is Polygon polygon)
        {
            var result = polygon.Factory.CreateMultiPolygon([polygon]);
            result.SRID = Srid;
            return result;
        }
        if (geometry is MultiPolygon multiPolygon)
        {
            multiPolygon.SRID = Srid;
            return multiPolygon;
        }
        throw new InvalidOperationException(
            $"Authorization union must be Polygon/MultiPolygon, got {geometry.GeometryType}.");
    }

    private static void RequireSource(
        string sourceKey,
        string sourceId,
        string capturedAt,
        string geometrySource,
        string what)
    {
        Require(!string.IsNullOrWhiteSpace(sourceKey)
                && !string.IsNullOrWhiteSpace(sourceId)
                && !string.IsNullOrWhiteSpace(geometrySource),
            $"Source metadata is incomplete for {what}.");
        Require(DateOnly.TryParseExact(capturedAt, "yyyy-MM-dd", out _),
            $"Capture date for {what} must be yyyy-MM-dd.");
        Require(capturedAt == DemoData.FixtureSnapshotDate,
            $"Capture date for {what} must match the locked {DemoData.FixtureSnapshotDate} snapshot.");
    }

    private static bool Confirm(string database)
    {
        Console.Write($"Type the database name ('{database}') to confirm, or anything else to abort: ");
        return string.Equals(Console.ReadLine()?.Trim(), database, StringComparison.Ordinal);
    }

    private static async Task WipeAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE " + string.Join(", ", Tables) + ";");
        await db.Database.ExecuteSqlRawAsync(string.Join("\n",
            Sequences.Select(sequence => $"ALTER SEQUENCE {sequence} RESTART WITH 1;")));
    }

    private static async Task<Dictionary<string, int>> InsertUsersAsync(AppDbContext db)
    {
        var joined = DateTime.UtcNow.AddDays(-180);
        var rows = DemoData.Users.Select((user, index) => new User
        {
            Username = user.Username,
            PasswordHash = BC.HashPassword(DemoData.Password),
            CreatedAt = joined.AddDays(index * 4),
        }).ToList();
        db.Users.AddRange(rows);
        await db.SaveChangesAsync();
        return rows.ToDictionary(user => user.Username, user => user.Id, StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, int>> InsertRolesAsync(AppDbContext db)
    {
        db.Roles.AddRange(DemoData.Roles.Select(role => new Role
        {
            Name = role.Name,
            Description = role.Description,
            CreatedAt = DateTime.UtcNow.AddDays(-175),
        }));
        await db.SaveChangesAsync();
        return await db.Roles.ToDictionaryAsync(role => role.Name, role => role.Id);
    }

    private static async Task InsertGrantsAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, int> users,
        IReadOnlyDictionary<string, int> roles,
        IReadOnlyDictionary<string, int> permissions)
    {
        db.RolePermissions.AddRange(
            from role in DemoData.Roles
            from permission in role.Permissions
            select new RolePermission { RoleId = roles[role.Name], PermissionId = permissions[permission] });
        db.UserRoles.AddRange(DemoData.Users.Select(user => new UserRole
        {
            UserId = users[user.Username],
            RoleId = roles[user.Role],
        }));
        db.UserPermissions.AddRange(
            from user in DemoData.Users
            from permission in user.DirectPermissions
            select new UserPermission
            {
                UserId = users[user.Username],
                PermissionId = permissions[permission],
            });
        await db.SaveChangesAsync();
    }

    private static async Task InsertAreasAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, int> users,
        IReadOnlyDictionary<string, int> roles)
    {
        var boundaries = await db.Provinces.AsNoTracking()
            .ToDictionaryAsync(province => province.Name, province => province.Geom, StringComparer.Ordinal);
        foreach (var role in DemoData.Roles.Where(role => role.AreaProvinceNames is not null))
            db.GeoAuthorizations.Add(new GeoAuthorization
            {
                RoleId = roles[role.Name],
                Geom = BuildArea(role.AreaProvinceNames!, boundaries),
            });
        foreach (var user in DemoData.Users.Where(user => user.AreaProvinceNames is not null))
            db.GeoAuthorizations.Add(new GeoAuthorization
            {
                UserId = users[user.Username],
                Geom = BuildArea(user.AreaProvinceNames!, boundaries),
            });
        await db.SaveChangesAsync();
    }

    private static async Task<Dictionary<string, int>> InsertCategoriesAsync(AppDbContext db, int adminId)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var pending = DemoData.Categories.ToList();
        while (pending.Count > 0)
        {
            var level = pending.Where(category =>
                category.Parent is null || ids.ContainsKey(category.Parent)).ToList();
            if (level.Count == 0)
                throw new InvalidOperationException("POI category tree has an unresolved parent.");
            var rows = level.Select(category => new PoiCategory
            {
                Name = category.Name,
                ParentId = category.Parent is null ? null : ids[category.Parent],
                Color = category.Color,
                IconKey = category.IconKey,
                UserId = adminId,
                ModifiedUserId = adminId,
                CreatedAt = DateTime.UtcNow.AddDays(-160),
            }).ToList();
            db.PoiCategories.AddRange(rows);
            await db.SaveChangesAsync();
            foreach (var (category, row) in level.Zip(rows)) ids[category.Name] = row.Id;
            pending = pending.Except(level).ToList();
        }
        return ids;
    }

    private static async Task InsertShapesAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, int> users)
    {
        foreach (var shape in DemoData.Shapes)
        {
            var ownerId = users[shape.Owner];
            var createdAt = DateTime.UtcNow.AddDays(-shape.DaysAgo);
            switch (shape.Type)
            {
                case "point":
                    db.Points.Add(new PointFeature
                    {
                        UserId = ownerId, Name = shape.Name, Color = shape.Color,
                        Geom = Parse(shape.Wkt, OgcGeometryType.Point, shape.Name),
                        CreatedAt = createdAt, ModifiedUserId = ownerId,
                    });
                    break;
                case "line":
                    db.Lines.Add(new LineFeature
                    {
                        UserId = ownerId, Name = shape.Name, Color = shape.Color,
                        Geom = Parse(shape.Wkt, OgcGeometryType.LineString, shape.Name),
                        CreatedAt = createdAt, ModifiedUserId = ownerId,
                    });
                    break;
                case "polygon":
                    db.Polygons.Add(new PolygonFeature
                    {
                        UserId = ownerId, Name = shape.Name, Color = shape.Color,
                        Geom = Parse(shape.Wkt, OgcGeometryType.Polygon, shape.Name),
                        CreatedAt = createdAt, ModifiedUserId = ownerId,
                    });
                    break;
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task InsertPoisAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, int> users,
        IReadOnlyDictionary<string, int> categories)
    {
        db.Pois.AddRange(DemoData.Pois.Select(poi => new Poi
        {
            UserId = users[poi.Owner],
            Name = poi.Name,
            CategoryId = categories[poi.Category],
            Geom = Parse(poi.Wkt, OgcGeometryType.Point, poi.Name),
            OpenTime = poi.Open,
            CloseTime = poi.Close,
            CreatedAt = DateTime.UtcNow.AddDays(-poi.DaysAgo),
            ModifiedUserId = users[poi.Owner],
        }));
        await db.SaveChangesAsync();
    }

    private static async Task InsertTransportationAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, int> users)
    {
        var routeRows = DemoData.Routes.Select(route => new TransportRoute
        {
            Name = route.Name,
            Color = route.Color,
            UserId = users[route.Owner],
            Geometry = (LineString)Parse(route.GeometryWkt, OgcGeometryType.LineString, route.Name),
            DistanceMeters = route.DistanceMeters,
            DurationSeconds = route.DurationSeconds,
            IsGeometryStale = false,
            RoutingErrorCode = null,
            CreatedAt = DateTime.UtcNow.AddDays(-route.DaysAgo),
            ModifiedUserId = users[route.Owner],
        }).ToList();
        db.Routes.AddRange(routeRows);
        await db.SaveChangesAsync();
        var byName = DemoData.Routes.Zip(routeRows)
            .ToDictionary(pair => pair.First.Name, pair => pair.Second, StringComparer.Ordinal);
        var orders = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var stop in DemoData.Stops)
        {
            var route = byName[stop.Route];
            var order = orders[stop.Route] = orders.GetValueOrDefault(stop.Route) + 1;
            db.Stops.Add(new Stop
            {
                UserId = route.UserId,
                Name = stop.Name,
                Color = null,
                Geom = Parse(stop.Wkt, OgcGeometryType.Point, stop.Name),
                RouteId = route.Id,
                SequenceOrder = order,
                CreatedAt = route.CreatedAt.AddHours(order),
                ModifiedUserId = route.UserId,
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task BackdateModifiedDatesAsync(AppDbContext db) =>
        await db.Database.ExecuteSqlRawAsync(string.Join("\n",
            Tables.Select(table => $"UPDATE {table} SET modified_date = created_at;")));

    private static async Task AssertEveryFeatureIsInsideItsAreaAsync(AppDbContext db)
    {
        var areas = await db.GeoAuthorizations.ToListAsync();
        var userRoles = await db.UserRoles.ToListAsync();
        var usernames = await db.Users.ToDictionaryAsync(user => user.Id, user => user.Username);
        Geometry? EffectiveArea(int userId)
        {
            var own = areas.FirstOrDefault(area => area.UserId == userId);
            if (own is not null) return own.Geom;
            var roleIds = userRoles.Where(grant => grant.UserId == userId)
                .Select(grant => grant.RoleId).ToHashSet();
            return areas.Where(area => area.RoleId is not null && roleIds.Contains(area.RoleId.Value))
                .Select(area => area.Geom)
                .Aggregate((Geometry?)null, (current, next) => current is null ? next : current.Union(next));
        }

        var owned = new List<(int UserId, string Kind, string? Name, Geometry Geom)>();
        owned.AddRange((await db.Points.ToListAsync()).Select(row => (row.UserId, "point", row.Name, row.Geom)));
        owned.AddRange((await db.Lines.ToListAsync()).Select(row => (row.UserId, "line", row.Name, row.Geom)));
        owned.AddRange((await db.Polygons.ToListAsync()).Select(row => (row.UserId, "polygon", row.Name, row.Geom)));
        owned.AddRange((await db.Pois.ToListAsync()).Select(row => (row.UserId, "poi", row.Name, row.Geom)));
        owned.AddRange((await db.Stops.ToListAsync()).Select(row => (row.UserId, "stop", row.Name, row.Geom)));
        owned.AddRange((await db.Routes.Where(route => route.Geometry != null).ToListAsync())
            .Select(row => (row.UserId, "route", (string?)row.Name, (Geometry)row.Geometry!)));
        var escaped = owned.Where(item => EffectiveArea(item.UserId) is { } area && !area.Covers(item.Geom))
            .Select(item => $"{item.Kind} '{item.Name}' (owner {usernames[item.UserId]})").ToArray();
        Require(escaped.Length == 0,
            "Seeded features outside their owner's authorization: " + string.Join("; ", escaped));
    }

    private static async Task AssertDemoContractAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, int> users)
    {
        var persistedUsers = await db.Users.OrderBy(user => user.Id).ToListAsync();
        Require(persistedUsers.Count == DemoData.ExpectedUserCount
                && persistedUsers.Select(user => user.Id).SequenceEqual(Enumerable.Range(1, 19))
                && persistedUsers.Select(user => user.Username)
                    .SequenceEqual(DemoData.Users.Select(user => user.Username)),
            "Persisted account order differs from the deterministic 1..19 contract.");
        Require(users.Count == DemoData.ExpectedUserCount, "Inserted user lookup is incomplete.");
        var persistedRoles = await db.Roles.ToListAsync();
        var expectedRoleNames = new HashSet<string>(
            [
                SeedData.AdminRoleName,
                SeedData.OperatorRoleName,
                SeedData.ViewerRoleName,
                .. DemoData.Roles.Select(role => role.Name),
            ],
            StringComparer.Ordinal);
        Require(persistedRoles.Count == 5
                && expectedRoleNames.SetEquals(persistedRoles.Select(role => role.Name)),
            "Persisted role names differ from the five-role manifest.");

        var persistedPermissions = await db.Permissions.ToListAsync();
        Require(SeedData.Permissions.Select(permission => permission.Name)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(persistedPermissions.Select(permission => permission.Name)),
            "Persisted permission names differ from the manifest.");
        var permissionById = persistedPermissions.ToDictionary(
            permission => permission.Id, permission => permission.Name);
        var roleById = persistedRoles.ToDictionary(role => role.Id, role => role.Name);
        var userById = persistedUsers.ToDictionary(user => user.Id, user => user.Username);

        var actualUserRoles = (await db.UserRoles.ToListAsync())
            .Select(grant => $"{userById[grant.UserId]}|{roleById[grant.RoleId]}")
            .ToHashSet(StringComparer.Ordinal);
        var expectedUserRoles = DemoData.Users
            .Select(user => $"{user.Username}|{user.Role}")
            .ToHashSet(StringComparer.Ordinal);
        Require(actualUserRoles.SetEquals(expectedUserRoles)
                && actualUserRoles.Count == DemoData.ExpectedUserCount,
            "Persisted user-role mappings differ from the manifest.");

        var expectedRolePermissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in persistedPermissions)
            expectedRolePermissions.Add($"{SeedData.AdminRoleName}|{permission.Name}");
        foreach (var permission in SeedData.OperatorPermissions)
            expectedRolePermissions.Add($"{SeedData.OperatorRoleName}|{permission}");
        foreach (var role in DemoData.Roles)
        foreach (var permission in role.Permissions)
            expectedRolePermissions.Add($"{role.Name}|{permission}");
        var actualRolePermissions = (await db.RolePermissions.ToListAsync())
            .Select(grant => $"{roleById[grant.RoleId]}|{permissionById[grant.PermissionId]}")
            .ToHashSet(StringComparer.Ordinal);
        Require(actualRolePermissions.SetEquals(expectedRolePermissions),
            "Persisted role-permission mappings differ from the manifest.");

        var expectedDirectPermissions = (
            from user in DemoData.Users
            from permission in user.DirectPermissions
            select $"{user.Username}|{permission}"
        ).ToHashSet(StringComparer.Ordinal);
        var actualDirectPermissions = (await db.UserPermissions.ToListAsync())
            .Select(grant => $"{userById[grant.UserId]}|{permissionById[grant.PermissionId]}")
            .ToHashSet(StringComparer.Ordinal);
        Require(actualDirectPermissions.SetEquals(expectedDirectPermissions),
            "Persisted direct user-permission mappings differ from the manifest.");

        Require(await db.GeoAuthorizations.CountAsync() == DemoData.ExpectedAreaCount,
            $"Authorization-area count must be {DemoData.ExpectedAreaCount}.");
        Require(await db.PoiCategories.CountAsync() == DemoData.ExpectedCategoryCount,
            $"POI category count must be {DemoData.ExpectedCategoryCount}.");
        Require(await db.Provinces.CountAsync() == DemoData.ExpectedProvinceCount,
            $"Province count must be {DemoData.ExpectedProvinceCount}.");
        Require(await db.Points.CountAsync() == DemoData.ExpectedPointCount
                && await db.Lines.CountAsync() == DemoData.ExpectedLineCount
                && await db.Polygons.CountAsync() == DemoData.ExpectedPolygonCount,
            "Persisted shape type counts differ from 218/60/50.");
        Require(await db.Pois.CountAsync() == DemoData.ExpectedPoiCount,
            $"POI count must be {DemoData.ExpectedPoiCount}.");
        var persistedRoutes = await db.Routes.OrderBy(route => route.Id).ToListAsync();
        Require(persistedRoutes.Count == DemoData.ExpectedRouteCount
                && persistedRoutes.Select(route => route.Id)
                    .SequenceEqual(Enumerable.Range(1, DemoData.ExpectedRouteCount))
                && persistedRoutes.Select(route => route.Name)
                    .SequenceEqual(DemoData.Routes.Select(route => route.Name)),
            "Persisted route ids/order differ from the deterministic 1..30 manifest.");
        Require(await db.Stops.CountAsync() == DemoData.ExpectedStopCount,
            $"Stop count must be {DemoData.ExpectedStopCount}.");
        Require(persistedRoutes.All(route =>
                route.Geometry != null && route.DistanceMeters > 0 && route.DurationSeconds > 0
                && !route.IsGeometryStale && route.RoutingErrorCode == null),
            "Every seeded route must be immediately healthy with persisted geometry and metrics.");
        var stops = await db.Stops.ToListAsync();
        foreach (var route in persistedRoutes)
        {
            var orders = stops.Where(stop => stop.RouteId == route.Id)
                .Select(stop => stop.SequenceOrder).OrderBy(order => order).ToArray();
            Require(orders.SequenceEqual(Enumerable.Range(1, orders.Length)),
                $"Route '{route.Name}' stop order is not contiguous.");
        }
    }

    private static async Task LogCurrentContentsAsync(AppDbContext db, ILogger logger)
    {
        logger.LogWarning(
            "Currently: {Users} users, {Roles} roles, {Shapes} shapes, {Pois} POIs, " +
            "{Categories} POI categories, {Routes} routes, {Stops} stops.",
            await db.Users.IgnoreQueryFilters().CountAsync(),
            await db.Roles.IgnoreQueryFilters().CountAsync(),
            await db.Points.IgnoreQueryFilters().CountAsync()
                + await db.Lines.IgnoreQueryFilters().CountAsync()
                + await db.Polygons.IgnoreQueryFilters().CountAsync(),
            await db.Pois.IgnoreQueryFilters().CountAsync(),
            await db.PoiCategories.IgnoreQueryFilters().CountAsync(),
            await db.Routes.IgnoreQueryFilters().CountAsync(),
            await db.Stops.IgnoreQueryFilters().CountAsync());
    }

    private static async Task LogSummaryAsync(
        AppDbContext db,
        ILogger logger)
    {
        var users = await db.Users.ToDictionaryAsync(user => user.Username, user => user.Id);
        logger.LogInformation(
            "Demo data seeded: {Users} users, {Points} points, {Lines} lines, {Polygons} polygons, " +
            "{Areas} areas, {Categories} categories, {Pois} POIs, {Routes} routes, {Stops} stops.",
            await db.Users.CountAsync(), await db.Points.CountAsync(), await db.Lines.CountAsync(),
            await db.Polygons.CountAsync(), await db.GeoAuthorizations.CountAsync(),
            await db.PoiCategories.CountAsync(), await db.Pois.CountAsync(),
            await db.Routes.CountAsync(), await db.Stops.CountAsync());
        foreach (var persona in DemoData.Users)
            logger.LogInformation("  {Id,2}  {Username,-22} {Role,-18} {Demonstrates}",
                users[persona.Username], persona.Username, persona.Role, persona.Demonstrates);
        logger.LogInformation("All accounts use the password '{Password}'.", DemoData.Password);
    }

    private static Geometry Parse(string wkt, OgcGeometryType expected, string what)
    {
        var geometry = Reader.Read(wkt);
        if (geometry is null || geometry.IsEmpty || geometry.OgcGeometryType != expected)
            throw new InvalidOperationException($"{what}: expected {expected} WKT.");
        if (!geometry.IsValid)
            throw new InvalidOperationException($"{what}: geometry is invalid.");
        geometry.SRID = Srid;
        return geometry;
    }

    private static void RequireUnique(IEnumerable<string> values, string what)
    {
        var duplicates = values.GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        Require(duplicates.Length == 0, $"Duplicate {what}: {string.Join(", ", duplicates)}.");
    }

    private static void RequireDictionaryEqual(
        IReadOnlyDictionary<string, int> expected,
        IReadOnlyDictionary<string, int> actual,
        string what)
    {
        Require(expected.Count == actual.Count
                && expected.All(pair => actual.GetValueOrDefault(pair.Key) == pair.Value),
            $"{what} differs from the manifest contract.");
    }

    private static bool IsTopologicalDuplicate(Geometry first, Geometry second) =>
        first.EnvelopeInternal.Equals(second.EnvelopeInternal)
        && first.EqualsTopologically(second);

    // All fixture coordinates use geographic EPSG:4326. Project each line segment into a local
    // equirectangular plane around the point so the 100-metre rule is expressed in metres rather
    // than a latitude-dependent degree approximation.
    private static double DistanceToLineMeters(Point point, LineString line)
    {
        const double earthRadiusMeters = 6_371_008.8;
        var longitudeScale = Math.Cos(point.Y * Math.PI / 180);
        var radians = Math.PI / 180;
        var minimum = double.PositiveInfinity;
        var coordinates = line.Coordinates;
        for (var index = 1; index < coordinates.Length; index++)
        {
            var first = coordinates[index - 1];
            var second = coordinates[index];
            var firstX = (first.X - point.X) * longitudeScale * earthRadiusMeters * radians;
            var firstY = (first.Y - point.Y) * earthRadiusMeters * radians;
            var secondX = (second.X - point.X) * longitudeScale * earthRadiusMeters * radians;
            var secondY = (second.Y - point.Y) * earthRadiusMeters * radians;
            var deltaX = secondX - firstX;
            var deltaY = secondY - firstY;
            var lengthSquared = deltaX * deltaX + deltaY * deltaY;
            var fraction = lengthSquared == 0
                ? 0
                : Math.Clamp(-(firstX * deltaX + firstY * deltaY) / lengthSquared, 0, 1);
            var closestX = firstX + fraction * deltaX;
            var closestY = firstY + fraction * deltaY;
            minimum = Math.Min(minimum, Math.Sqrt(closestX * closestX + closestY * closestY));
        }

        return minimum;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
