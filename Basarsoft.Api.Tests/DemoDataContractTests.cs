using System.Text.Json;
using Basarsoft.Api.Data;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
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
    ];

    [Fact]
    public void ValidateManifest_AcceptsTheRepositoryScenario()
    {
        // Exercise the exact preflight used by `dotnet run -- seed-demo`.
        DemoSeeder.ValidateManifest();
    }

    [Fact]
    public void HeadlineCountsAndAccountOrder_AreDeterministic()
    {
        Assert.Equal(17, DemoData.ExpectedUserCount);
        Assert.Equal(164, DemoData.ExpectedShapeCount);
        Assert.Equal(139, DemoData.ExpectedPoiCount);
        Assert.Equal(42, DemoData.ExpectedCategoryCount);
        Assert.Equal(15, DemoData.ExpectedAreaCount);
        Assert.Equal(81, DemoData.ExpectedProvinceCount);
        Assert.Equal("secret123", DemoData.Password);

        Assert.Equal(DemoData.ExpectedUserCount, DemoData.Users.Count);
        Assert.Equal(OrderedAccounts, DemoData.Users.Select(user => user.Username));
        Assert.Equal(DemoData.ExpectedShapeCount, DemoData.Shapes.Count + DemoData.Provinces.Count);
        Assert.Equal(DemoData.ExpectedPoiCount, DemoData.Pois.Count + DemoData.Provinces.Count);
        Assert.Equal(DemoData.ExpectedCategoryCount, DemoData.Categories.Count);
        Assert.Equal(DemoData.ExpectedAreaCount,
            DemoData.Users.Count(user => user.AreaWkt is not null)
            + DemoData.Roles.Count(role => role.AreaWkt is not null));
        Assert.Equal(DemoData.ExpectedProvinceCount, DemoData.Provinces.Count);
    }

    [Fact]
    public void Shapes_HaveTheApprovedOwnerAndTypeDistribution()
    {
        var ownerCounts = DemoData.Shapes
            .GroupBy(shape => shape.Owner)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        ownerCounts["admin"] += DemoData.Provinces.Count;

        var expectedOwners = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["admin"] = 100,
            ["marmara_manager"] = 7,
            ["aegean_manager"] = 7,
            ["mediterranean_manager"] = 7,
            ["central_manager"] = 7,
            ["blacksea_manager"] = 7,
            ["eastern_manager"] = 7,
            ["southeast_manager"] = 7,
            ["ankara_editor"] = 6,
            ["istanbul_editor"] = 2,
            ["izmir_editor"] = 2,
            ["antalya_editor"] = 2,
            ["viewer"] = 3,
        };

        AssertDictionaryEqual(expectedOwners, ownerCounts);
        Assert.Equal(109,
            DemoData.Shapes.Count(shape => shape.Type == "point") + DemoData.Provinces.Count);
        Assert.Equal(30, DemoData.Shapes.Count(shape => shape.Type == "line"));
        Assert.Equal(25, DemoData.Shapes.Count(shape => shape.Type == "polygon"));
    }

    [Fact]
    public void Pois_HaveTheApprovedOwnershipAndLeafCategoryDistribution()
    {
        var ownerCounts = DemoData.Pois
            .GroupBy(poi => poi.Owner)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        ownerCounts["admin"] += DemoData.Provinces.Count;

        AssertDictionaryEqual(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["admin"] = 106,
                ["istanbul_operator"] = 12,
                ["antalya_operator"] = 7,
                ["gaziantep_operator"] = 7,
                ["trabzon_operator"] = 7,
            },
            ownerCounts);

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

        // The per-category expectation is manifest-derived (curated + template rotation) and shared
        // with the seeder's DB verification; here we assert its structural invariants.
        var expectedCategoryUse = DemoSeeder.ExpectedPoiCategoryUse();
        Assert.True(expectedCategoryUse.Keys.All(leaves.Contains));
        Assert.All(leaves, leaf => Assert.True(expectedCategoryUse.GetValueOrDefault(leaf) > 0,
            $"Leaf category '{leaf}' is never used by a curated or generated POI."));
        Assert.Equal(DemoData.ExpectedPoiCount, expectedCategoryUse.Values.Sum());

        // The rotation spreads the 12 templates evenly over the 81 provinces (each used 6-7 times).
        var templateUse = Enumerable.Range(0, DemoData.ExpectedProvinceCount)
            .GroupBy(index => index % DemoData.ProvincePoiTemplates.Count)
            .Select(group => group.Count())
            .ToList();
        Assert.Equal(DemoData.ProvincePoiTemplates.Count, templateUse.Count);
        Assert.All(templateUse, count => Assert.InRange(count, 6, 7));
    }

    [Fact]
    public void ProvinceManifest_HasSevenRegionsAndExactlyMatchesGeoJson()
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
        var actualRegions = DemoData.Provinces
            .GroupBy(province => province.Region)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        AssertDictionaryEqual(expectedRegions, actualRegions);

        var sourcePath = Path.Combine(AppContext.BaseDirectory, "Data", "provinces.geojson");
        using var document = JsonDocument.Parse(File.ReadAllText(sourcePath));
        var sourceNames = document.RootElement
            .GetProperty("features")
            .EnumerateArray()
            .Select(feature => feature.GetProperty("properties").GetProperty("name").GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
        var manifestNames = DemoData.Provinces.Select(province => province.Name).ToArray();

        Assert.Equal(DemoData.ExpectedProvinceCount, sourceNames.Length);
        Assert.Equal(sourceNames.Length, sourceNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            manifestNames.OrderBy(name => name, StringComparer.Ordinal),
            sourceNames.OrderBy(name => name, StringComparer.Ordinal));

        var geoJsonOptions = new JsonSerializerOptions();
        geoJsonOptions.Converters.Add(new GeoJsonConverterFactory());
        var sourceByName = document.RootElement
            .GetProperty("features")
            .EnumerateArray()
            .ToDictionary(
                feature => feature.GetProperty("properties").GetProperty("name").GetString()!,
                feature => feature,
                StringComparer.Ordinal);
        for (var index = 0; index < DemoData.Provinces.Count; index++)
        {
            var province = DemoData.Provinces[index];
            var feature = sourceByName[province.Name];
            var boundary = JsonSerializer.Deserialize<Geometry>(
                feature.GetProperty("geometry").GetRawText(),
                geoJsonOptions);
            Assert.NotNull(boundary);
            if (!boundary.IsValid)
                boundary = GeometryFixer.Fix(boundary);
            boundary.SRID = 4326;

            var first = DemoSeeder.DeriveProvincePoints(boundary, index, province.Name);
            var second = DemoSeeder.DeriveProvincePoints(boundary, index, province.Name);
            Assert.True(boundary.Covers(first.Marker), $"{province.Name} marker escaped its boundary.");
            Assert.True(boundary.Covers(first.Hub), $"{province.Name} hub escaped its boundary.");
            Assert.True(first.Marker.Distance(first.Hub) > 1e-12,
                $"{province.Name} marker and hub overlap.");
            Assert.True(first.Marker.EqualsExact(second.Marker)
                        && first.Hub.EqualsExact(second.Hub),
                $"{province.Name} derived points are not deterministic.");
        }
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
            [SeedData.OperatorRoleName] =
                SeedData.OperatorPermissions.ToHashSet(StringComparer.Ordinal),
            [SeedData.ViewerRoleName] = new(StringComparer.Ordinal),
        };
        foreach (var role in DemoData.Roles)
            rolePermissions[role.Name] = role.Permissions.ToHashSet(StringComparer.Ordinal);

        Assert.Equal(5, rolePermissions.Count);
        AssertSetEqual(["add_point", "add_line", "add_polygon"],
            rolePermissions[DemoData.RegionalManagerRoleName]);
        AssertSetEqual(["add_point", "add_line"], rolePermissions[DemoData.EditorRoleName]);
        AssertSetEqual(["add_poi"], rolePermissions[SeedData.OperatorRoleName]);
        Assert.Empty(rolePermissions[SeedData.ViewerRoleName]);

        foreach (var user in DemoData.Users)
        {
            var inherited = rolePermissions[user.Role];
            Assert.Empty(user.DirectPermissions.Intersect(inherited, StringComparer.Ordinal));

            var effective = inherited
                .Concat(user.DirectPermissions)
                .ToHashSet(StringComparer.Ordinal);
            var expected = user.Username switch
            {
                "admin" => allPermissions,
                "ankara_editor" => Set("add_point", "add_line", "add_polygon"),
                "istanbul_editor" or "izmir_editor" or "antalya_editor" =>
                    Set("add_point", "add_line"),
                "istanbul_operator" or "antalya_operator" or
                    "gaziantep_operator" or "trabzon_operator" => Set("add_poi"),
                "viewer" => Set(),
                _ => Set("add_point", "add_line", "add_polygon"),
            };
            AssertSetEqual(expected, effective);
        }

        Assert.Equal(["add_polygon"],
            DemoData.Users.Single(user => user.Username == "ankara_editor").DirectPermissions);
        Assert.All(
            DemoData.Users.Where(user => user.Username != "ankara_editor"),
            user => Assert.Empty(user.DirectPermissions));
    }

    private static HashSet<string> Set(params string[] values) =>
        values.ToHashSet(StringComparer.Ordinal);

    private static void AssertSetEqual(
        IEnumerable<string> expected,
        IEnumerable<string> actual) =>
        Assert.True(
            expected.ToHashSet(StringComparer.Ordinal).SetEquals(actual),
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
