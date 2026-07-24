using System.Text.Json;
using Basarsoft.Api.Data;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using Xunit;

namespace Basarsoft.Api.Tests;

public class DemoDataContractTests
{
    private static readonly string[] OrderedAccounts =
    [
        "admin",
        "marmara_manager",
        "aegean_manager",
        "mediterranean_manager",
        "central_manager",
        "blacksea_manager",
        "eastern_manager",
        "southeast_manager",
        "ankara_editor",
        "istanbul_editor",
        "izmir_editor",
        "antalya_editor",
        "istanbul_operator",
        "antalya_operator",
        "gaziantep_operator",
        "trabzon_operator",
        "viewer",
        "ankara_operator",
        "izmir_operator",
    ];

    [Fact]
    public void ValidateManifest_AcceptsTheCommittedOfflineFixtures()
    {
        // This is the exact preflight executed before seed-demo can enter its destructive transaction.
        DemoSeeder.ValidateManifest();
    }

    [Fact]
    public void HeadlineCountsAndAccountOrder_AreDeterministic()
    {
        Assert.Equal(19, DemoData.ExpectedUserCount);
        Assert.Equal(328, DemoData.ExpectedShapeCount);
        Assert.Equal(218, DemoData.ExpectedPointCount);
        Assert.Equal(60, DemoData.ExpectedLineCount);
        Assert.Equal(50, DemoData.ExpectedPolygonCount);
        Assert.Equal(324, DemoData.ExpectedPoiCount);
        Assert.Equal(42, DemoData.ExpectedCategoryCount);
        Assert.Equal(17, DemoData.ExpectedAreaCount);
        Assert.Equal(81, DemoData.ExpectedProvinceCount);
        Assert.Equal(30, DemoData.ExpectedRouteCount);
        Assert.Equal(215, DemoData.ExpectedStopCount);
        Assert.Equal("secret123", DemoData.Password);

        Assert.Equal(DemoData.ExpectedUserCount, DemoData.Users.Count);
        Assert.Equal(OrderedAccounts, DemoData.Users.Select(user => user.Username));
        Assert.Equal(DemoData.ExpectedShapeCount, DemoData.Shapes.Count);
        Assert.Equal(DemoData.ExpectedPoiCount, DemoData.Pois.Count);
        Assert.Equal(DemoData.ExpectedCategoryCount, DemoData.Categories.Count);
        Assert.Equal(DemoData.ExpectedAreaCount,
            DemoData.Users.Count(user => user.AreaProvinceNames is not null)
            + DemoData.Roles.Count(role => role.AreaProvinceNames is not null));
        Assert.Equal(DemoData.ExpectedProvinceCount, DemoData.Provinces.Count);
        Assert.Equal(DemoData.ExpectedRouteCount, DemoData.Routes.Count);
        Assert.Equal(DemoData.ExpectedStopCount, DemoData.Stops.Count);
    }

    [Fact]
    public void Shapes_HaveTheApprovedOwnerTypeThemeAndProvenanceDistribution()
    {
        AssertDictionaryEqual(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["admin"] = 100,
                ["marmara_manager"] = 21,
                ["aegean_manager"] = 21,
                ["mediterranean_manager"] = 21,
                ["central_manager"] = 21,
                ["blacksea_manager"] = 21,
                ["eastern_manager"] = 21,
                ["southeast_manager"] = 21,
                ["ankara_editor"] = 20,
                ["istanbul_editor"] = 12,
                ["izmir_editor"] = 12,
                ["antalya_editor"] = 12,
                ["viewer"] = 25,
            },
            DemoData.Shapes.GroupBy(shape => shape.Owner)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));

        Assert.Equal(218, DemoData.Shapes.Count(shape => shape.Type == "point"));
        Assert.Equal(60, DemoData.Shapes.Count(shape => shape.Type == "line"));
        Assert.Equal(50, DemoData.Shapes.Count(shape => shape.Type == "polygon"));
        Assert.Equal(DemoData.Shapes.Count,
            DemoData.Shapes.Select(shape => shape.Name).Distinct(StringComparer.Ordinal).Count());

        foreach (var theme in DemoData.Themes)
        {
            Assert.Equal(12, DemoData.Shapes.Count(shape =>
                shape.Type == "line" && shape.Theme == theme.Key));
            Assert.Equal(10, DemoData.Shapes.Count(shape =>
                shape.Type == "polygon" && shape.Theme == theme.Key));
            Assert.All(DemoData.Shapes.Where(shape => shape.Theme == theme.Key),
                shape => Assert.Equal(theme.Color, shape.Color));
        }

        Assert.Equal(DemoData.Shapes.Count,
            DemoData.Shapes.Select(shape => $"{shape.SourceKey}:{shape.SourceId}")
                .Distinct(StringComparer.Ordinal).Count());
        Assert.All(DemoData.Shapes, shape =>
        {
            Assert.Equal(DemoData.FixtureSnapshotDate, shape.CapturedAt);
            Assert.False(string.IsNullOrWhiteSpace(shape.SourceName));
            Assert.False(string.IsNullOrWhiteSpace(shape.GeometrySource));
            Assert.InRange(shape.Name.Length, 1, 80);
        });
        Assert.DoesNotContain(DemoData.Shapes.Where(shape => shape.Type == "polygon"),
            shape => shape.Name.Contains("Province", StringComparison.OrdinalIgnoreCase)
                     || shape.Name.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Pois_AreNationwideLeafCategorizedAndSourceBacked()
    {
        Assert.All(DemoData.Pois, poi => Assert.Equal("admin", poi.Owner));
        Assert.Equal(DemoData.ExpectedPoiCount,
            DemoData.Pois.Select(poi => $"{poi.SourceKey}:{poi.SourceId}")
                .Distinct(StringComparer.Ordinal).Count());

        var parentNames = DemoData.Categories
            .Where(category => category.Parent is not null)
            .Select(category => category.Parent!)
            .ToHashSet(StringComparer.Ordinal);
        var leaves = DemoData.Categories
            .Where(category => !parentNames.Contains(category.Name))
            .Select(category => category.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(9, DemoData.Categories.Count(category => category.Parent is null));
        Assert.Equal(33, leaves.Count);
        Assert.All(DemoData.Pois, poi => Assert.Contains(poi.Category, leaves));

        var categoryUse = DemoSeeder.ExpectedPoiCategoryUse();
        Assert.All(leaves, leaf => Assert.True(categoryUse.GetValueOrDefault(leaf) >= 2,
            $"Leaf '{leaf}' has fewer than two real examples."));
        var provinceUse = DemoData.Pois.GroupBy(poi => poi.Province)
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
        Assert.All(DemoData.Provinces, province => Assert.Equal(
            3 + expectedExtras.GetValueOrDefault(province.Name),
            provinceUse.GetValueOrDefault(province.Name)));
        var byProvince = DemoData.Pois.GroupBy(poi => poi.Province)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var province in DemoData.Provinces)
        {
            var rows = byProvince[province.Name];
            Assert.Contains(rows, poi => poi.Category == "Hospital");
            Assert.Contains(rows, poi => poi.Category is "Restaurant" or "Cafe" or "Bakery" or "Fast Food");
            Assert.Contains(rows, poi => poi.Category is "Mall" or "Supermarket");
        }
        Assert.All(DemoData.Pois, poi =>
        {
            Assert.Equal("openstreetmap", poi.SourceKey);
            Assert.True(
                poi.SourceId.StartsWith("node/", StringComparison.Ordinal)
                || poi.SourceId.StartsWith("way/", StringComparison.Ordinal),
                $"POI '{poi.Name}' lost its canonical OSM object identity.");
            var expectedHours = DemoData.PoiHoursByCategory[poi.Category];
            Assert.Equal(expectedHours.Open, poi.Open);
            Assert.Equal(expectedHours.Close, poi.Close);
            Assert.Equal(DemoData.FixtureSnapshotDate, poi.CapturedAt);
            Assert.False(string.IsNullOrWhiteSpace(poi.GeometrySource));
        });
        Assert.All(DemoData.Pois.Where(poi => poi.Category == "Hospital"), poi =>
        {
            Assert.Equal(new TimeOnly(0, 0), poi.Open);
            Assert.Equal(new TimeOnly(23, 59), poi.Close);
        });

        var baselineByProvince = DemoData.Pois.Take(243)
            .GroupBy(poi => poi.Province)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        Assert.Equal(81, baselineByProvince.Count);
        foreach (var province in DemoData.Provinces)
        {
            var baseline = baselineByProvince[province.Name];
            Assert.Equal(3, baseline.Length);
            Assert.Single(baseline, poi => poi.Category == "Hospital");
            Assert.Single(baseline,
                poi => poi.Category is "Restaurant" or "Cafe" or "Bakery" or "Fast Food");
            Assert.Single(baseline, poi => poi.Category is "Mall" or "Supermarket");
        }
    }

    [Fact]
    public void PoiReviewManifest_IsDatedUniqueAndMinistryCrossChecked()
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory, "Data", "demo", "poi-baseline-selections.json");
        using var document = JsonDocument.Parse(File.ReadAllText(sourcePath));
        Assert.Equal(
            DemoData.FixtureSnapshotDate,
            document.RootElement.GetProperty("snapshot").GetString());

        var selections = document.RootElement.GetProperty("selections").EnumerateArray().ToArray();
        Assert.Equal(67, selections.Length);
        Assert.Equal(
            selections.Length,
            selections.Select(selection =>
                    $"{selection.GetProperty("province").GetString()}:"
                    + selection.GetProperty("currentCategory").GetString() + ":"
                    + (selection.TryGetProperty("replacesSourceId", out var replaces)
                        ? replaces.GetString()
                        : "province-baseline"))
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            selections.Length,
            selections.Select(selection => selection.GetProperty("sourceId").GetString())
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(selections, selection =>
        {
            Assert.False(string.IsNullOrWhiteSpace(
                selection.GetProperty("expectedName").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(
                selection.GetProperty("reviewNote").GetString()));
        });

        var cityHospitals = selections.Where(selection =>
                selection.GetProperty("expectedName").GetString()!
                    .Contains("Şehir Hastanesi", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(5, cityHospitals.Length);
        Assert.All(cityHospitals, selection => Assert.StartsWith(
            "https://khgm.saglik.gov.tr/",
            selection.GetProperty("verificationUrl").GetString(),
            StringComparison.Ordinal));
        Assert.Contains(selections, selection =>
            selection.GetProperty("province").GetString() == "Hakkari"
            && selection.GetProperty("sourceId").GetString() == "way/363127799"
            && selection.GetProperty("expectedName").GetString() == "Hakkari Devlet Hastanesi");
    }

    [Fact]
    public void Transportation_HasTwentyFiveUrbanFiveIntercityAndHealthyGeometry()
    {
        AssertDictionaryEqual(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["İstanbul"] = 5,
                ["Ankara"] = 5,
                ["İzmir"] = 5,
                ["Antalya"] = 5,
                ["Bursa"] = 1,
                ["Adana"] = 1,
                ["Konya"] = 1,
                ["Gaziantep"] = 1,
                ["Trabzon"] = 1,
            },
            DemoData.Routes.Where(route => route.Kind == "urban")
                .GroupBy(route => route.City)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
        Assert.Equal(5, DemoData.Routes.Count(route => route.Kind == "intercity"));
        Assert.True(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "İstanbul|34BZ", "İstanbul|34AS", "İstanbul|500T", "İstanbul|15F", "İstanbul|25E",
                "Ankara|205", "Ankara|303", "Ankara|334-6", "Ankara|413", "Ankara|442",
                "İzmir|202", "İzmir|515", "İzmir|584", "İzmir|808", "İzmir|950",
                "Antalya|KL08", "Antalya|VS18", "Antalya|LC07A", "Antalya|ML22", "Antalya|VF63",
                "Bursa|38/B-2", "Adana|114", "Konya|4-A", "Gaziantep|B39", "Trabzon|121",
            }.SetEquals(DemoData.Routes.Where(route => route.Kind == "urban")
                .Select(route => $"{route.City}|{route.LineCode}")
                .ToHashSet(StringComparer.Ordinal)));
        Assert.True(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Intercity corridor · İstanbul–Ankara",
                "Intercity corridor · İstanbul–Bursa–İzmir",
                "Intercity corridor · Ankara–Konya–Antalya",
                "Intercity corridor · İzmir–Aydın–Muğla–Antalya",
                "Intercity corridor · Adana–Gaziantep–Şanlıurfa",
            }.SetEquals(DemoData.Routes.Where(route => route.Kind == "intercity")
                .Select(route => route.Name)
                .ToHashSet(StringComparer.Ordinal)));

        Assert.All(DemoData.Routes, route =>
        {
            Assert.True(route.DistanceMeters > 0);
            Assert.True(route.DurationSeconds > 0);
            Assert.True(route.DaysAgo >= 0);
            Assert.StartsWith("LINESTRING", route.GeometryWkt, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(DemoData.FixtureSnapshotDate, route.CapturedAt);
            Assert.True(
                Uri.TryCreate(route.SourceUrl, UriKind.Absolute, out var sourceUrl)
                && sourceUrl.Scheme is "http" or "https");
        });

        var stopGroups = DemoData.Stops.GroupBy(stop => stop.Route)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        Assert.Equal(DemoData.Routes.Count, stopGroups.Count);
        foreach (var route in DemoData.Routes)
        {
            var stops = stopGroups[route.Name];
            Assert.Equal(route.Kind == "urban" ? 8 : stops.Count, stops.Count);
            if (route.Kind == "intercity") Assert.InRange(stops.Count, 2, 4);
            Assert.Equal(stops.Count,
                stops.Select(stop => stop.SourceId).Distinct(StringComparer.Ordinal).Count());
            Assert.All(stops, stop =>
            {
                Assert.InRange(stop.Name.Length, 1, 80);
                Assert.True(stop.DaysAgo >= 0);
                Assert.Equal(DemoData.FixtureSnapshotDate, stop.CapturedAt);
                Assert.StartsWith("http", stop.SourceUrl, StringComparison.OrdinalIgnoreCase);
                Assert.False(string.IsNullOrWhiteSpace(stop.GeometrySource));
            });
        }
    }

    [Fact]
    public void ProvinceManifest_HasCoveredCapitalsAndExactlyMatchesGeoJson()
    {
        var expectedRegions = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Marmara"] = 11,
            ["Aegean"] = 8,
            ["Mediterranean"] = 8,
            ["Central Anatolia"] = 13,
            ["Black Sea"] = 18,
            ["Eastern Anatolia"] = 14,
            ["Southeastern Anatolia"] = 9,
        };
        AssertDictionaryEqual(expectedRegions, DemoData.Provinces
            .GroupBy(province => province.Region)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));

        var sourcePath = Path.Combine(AppContext.BaseDirectory, "Data", "provinces.geojson");
        using var document = JsonDocument.Parse(File.ReadAllText(sourcePath));
        var options = new JsonSerializerOptions();
        options.Converters.Add(new GeoJsonConverterFactory());
        var features = document.RootElement.GetProperty("features").EnumerateArray().ToArray();
        Assert.Equal(81, features.Length);
        var manifestByName = DemoData.Provinces.ToDictionary(province => province.Name, StringComparer.Ordinal);
        var boundarySourceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var feature in features)
        {
            var properties = feature.GetProperty("properties");
            var name = properties.GetProperty("name").GetString()!;
            var manifest = manifestByName[name];
            Assert.Equal(manifest.Region, properties.GetProperty("region").GetString());
            Assert.Equal(manifest.Color, properties.GetProperty("color").GetString());
            Assert.Equal(manifest.CapitalName, properties.GetProperty("capitalName").GetString());
            Assert.Equal(manifest.BoundarySourceId,
                properties.GetProperty("boundarySourceId").GetString());
            Assert.StartsWith("relation/", manifest.BoundarySourceId, StringComparison.Ordinal);
            Assert.True(boundarySourceIds.Add(manifest.BoundarySourceId),
                $"Duplicate boundary source id {manifest.BoundarySourceId}.");
            Assert.Equal(DemoData.FixtureSnapshotDate, manifest.CapturedAt);

            var boundary = JsonSerializer.Deserialize<Geometry>(
                feature.GetProperty("geometry").GetRawText(), options)!;
            Assert.IsType<MultiPolygon>(boundary);
            Assert.True(boundary.IsValid, $"{name} has invalid source geometry.");
            var capital = new Point(manifest.CapitalLongitude, manifest.CapitalLatitude);
            Assert.True(boundary.Covers(capital),
                $"{manifest.CapitalName} is outside {manifest.Name}.");
        }
        Assert.Equal(81, boundarySourceIds.Count);
        Assert.Equal("İzmit", manifestByName["Kocaeli"].CapitalName);
        Assert.Equal("Adapazarı", manifestByName["Sakarya"].CapitalName);
        Assert.Equal("Antakya", manifestByName["Hatay"].CapitalName);
    }

    [Fact]
    public void PermissionMatrix_IsExactAndHasNoRedundantDirectGrants()
    {
        var allPermissions = SeedData.Permissions
            .Select(permission => permission.Name)
            .ToHashSet(StringComparer.Ordinal);
        var rolePermissions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [SeedData.AdminRoleName] = allPermissions,
            [SeedData.OperatorRoleName] = SeedData.OperatorPermissions.ToHashSet(StringComparer.Ordinal),
            [SeedData.ViewerRoleName] = [],
        };
        foreach (var role in DemoData.Roles)
            rolePermissions[role.Name] = role.Permissions.ToHashSet(StringComparer.Ordinal);

        Assert.Equal(5, rolePermissions.Count);
        AssertSetEqual(["add_point", "add_line", "add_polygon"],
            rolePermissions[DemoData.RegionalManagerRoleName]);
        AssertSetEqual(["add_point", "add_line"], rolePermissions[DemoData.EditorRoleName]);
        AssertSetEqual(["manage_transport"], rolePermissions[SeedData.OperatorRoleName]);
        Assert.Empty(rolePermissions[SeedData.ViewerRoleName]);

        foreach (var user in DemoData.Users)
        {
            var inherited = rolePermissions[user.Role];
            Assert.Empty(user.DirectPermissions.Intersect(inherited, StringComparer.Ordinal));
            var effective = inherited.Concat(user.DirectPermissions).ToHashSet(StringComparer.Ordinal);
            var expected = user.Username switch
            {
                "admin" => allPermissions,
                "ankara_editor" => Set("add_point", "add_line", "add_polygon"),
                "istanbul_editor" or "izmir_editor" or "antalya_editor" =>
                    Set("add_point", "add_line"),
                "istanbul_operator" or "antalya_operator" or "gaziantep_operator"
                    or "trabzon_operator" or "ankara_operator" or "izmir_operator" =>
                    Set("manage_transport"),
                "viewer" => Set(),
                _ => Set("add_point", "add_line", "add_polygon"),
            };
            AssertSetEqual(expected, effective);
        }
        Assert.Equal(["add_polygon"],
            DemoData.Users.Single(user => user.Username == "ankara_editor").DirectPermissions);
        Assert.All(DemoData.Users.Where(user => user.Username != "ankara_editor"),
            user => Assert.Empty(user.DirectPermissions));
    }

    private static HashSet<string> Set(params string[] values) =>
        values.ToHashSet(StringComparer.Ordinal);

    private static void AssertSetEqual(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.True(expected.ToHashSet(StringComparer.Ordinal).SetEquals(actual),
            $"Expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}].");

    private static void AssertDictionaryEqual(
        IReadOnlyDictionary<string, int> expected,
        IReadOnlyDictionary<string, int> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (key, count) in expected)
            Assert.Equal(count, actual.GetValueOrDefault(key));
    }
}
