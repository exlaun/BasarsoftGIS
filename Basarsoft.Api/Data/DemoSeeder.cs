using Basarsoft.Api.Models;
using Basarsoft.Api.Services;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.Text.Json;
using BC = BCrypt.Net.BCrypt;

namespace Basarsoft.Api.Data;

// DESTRUCTIVE. Wipes every application table and rebuilds the demo dataset described in DemoData.
// Reached only through `dotnet run -- seed-demo`, only in Development — never from a normal startup.
//
// Re-runnable by construction: it begins by truncating and restarting every id sequence, so two runs
// produce identical ids and the demo script can rely on them.
public static class DemoSeeder
{
    // Every table holding application data. Postgres truncates a mutually-referencing group in one
    // statement as long as every referencing table is listed, so FK order is irrelevant here — but
    // CASCADE is deliberately NOT used: a table added later and forgotten in this list then makes
    // the seeder fail loudly instead of quietly leaving orphaned rows behind.
    private static readonly string[] Tables =
    [
        "user_permissions", "role_permissions", "user_roles",
        "tbl_geo_authorization", "tbl_poi", "tbl_poi_category",
        "tbl_location_analysis_criterion", "tbl_location_analysis",
        "tbl_point", "tbl_line", "tbl_polygon",
        "permissions", "roles", "users",
        // tbl_province is deliberately absent: static reference data with no FK into the demo tables
        // (analysis runs reference it, and those are truncated above). ProvinceSeeder below fills it
        // if this is a brand-new database.
    ];

    // TRUNCATE ... RESTART IDENTITY cannot be used to reset these: only 9 of the 12 are OWNED BY
    // their column (the AddGeoAuthorization and AddPoiTables migrations never issued ALTER SEQUENCE
    // ... OWNED BY), so it would restart nine and silently leave three running on — users beginning
    // at 1 while POIs carried on from wherever development left them.
    private static readonly string[] Sequences =
    [
        "seq_users", "seq_roles", "seq_permissions",
        "seq_user_roles", "seq_role_permissions", "seq_user_permissions",
        "seq_tbl_point", "seq_tbl_line", "seq_tbl_polygon",
        "seq_tbl_geo_authorization", "seq_tbl_poi_category", "seq_tbl_poi",
        "seq_tbl_location_analysis", "seq_tbl_location_analysis_criterion",
        // seq_tbl_province keeps running: its table is not wiped (see Tables above).
    ];

    private const int Srid = 4326;

    private static readonly WKTReader Reader = new();

    private sealed record ProvinceSeedPoint(
        DemoData.DemoProvince Manifest,
        Geometry Boundary,
        Point Marker,
        Point Hub);

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
            await ValidateProvinceSourceManifestAsync();
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
            // One transaction around the wipe and every insert (TRUNCATE is transactional in
            // Postgres), so a failure half-way leaves the database as it was rather than empty.
            await using var tx = await db.Database.BeginTransactionAsync();

            await WipeAsync(db);

            // Against empty tables, AdminSeeder takes its first-creation path in every branch — the
            // only way to get an Admin role holding the whole permission catalogue. It also creates
            // the Operator and Viewer roles two of the personas use. Nothing below re-grants any of
            // that; a second grant would violate the (role_id, permission_id) unique index.
            await AdminSeeder.SeedAsync(db);

            // Provinces are not wiped (reference data), but a brand-new demo database still needs
            // them for the location-analysis dropdown; the seeder no-ops when they already exist.
            await ProvinceSeeder.SeedAsync(db);

            var provincePoints = await LoadProvinceSeedPointsAsync(db);
            var permissions = await db.Permissions.ToDictionaryAsync(p => p.Name, p => p.Id);

            var users = await InsertUsersAsync(db);
            var roles = await InsertRolesAsync(db);
            await InsertGrantsAsync(db, users, roles, permissions);
            await InsertAreasAsync(db, users, roles);
            var categories = await InsertCategoriesAsync(db, users["admin"]);
            await InsertShapesAsync(db, users, provincePoints);
            await InsertPoisAsync(db, users, categories, provincePoints);

            await BackdateModifiedDatesAsync(db);
            await AssertEveryShapeIsInsideItsAreaAsync(db);
            await AssertDemoContractAsync(db, users, provincePoints);

            await tx.CommitAsync();

            await LogSummaryAsync(db, logger, users);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Demo seed FAILED. The database was rolled back to its previous contents.");
            return false;
        }
    }

    private static bool Confirm(string database)
    {
        Console.Write($"Type the database name ('{database}') to confirm, or anything else to abort: ");
        return string.Equals(Console.ReadLine()?.Trim(), database, StringComparison.Ordinal);
    }

    // Validates the code-owned part of the scenario before the destructive transaction can begin.
    // Kept public so the API contract tests exercise the exact same rules as seed-demo.
    public static void ValidateManifest()
    {
        string[] expectedUsers =
        [
            "admin",
            "marmara_manager", "aegean_manager", "mediterranean_manager", "central_manager",
            "blacksea_manager", "eastern_manager", "southeast_manager",
            "ankara_editor", "istanbul_editor", "izmir_editor", "antalya_editor",
            "istanbul_operator", "antalya_operator", "gaziantep_operator", "trabzon_operator",
            "viewer",
        ];

        Require(DemoData.Password == "secret123", "All demo accounts must use password 'secret123'.");
        Require(DemoData.Users.Count == DemoData.ExpectedUserCount,
            $"Expected {DemoData.ExpectedUserCount} users, found {DemoData.Users.Count}.");
        Require(DemoData.Users.Select(u => u.Username).SequenceEqual(expectedUsers),
            "Demo account order no longer matches the deterministic ids 1..17.");
        RequireUnique(DemoData.Users.Select(u => u.Username), "usernames");

        var permissionNames = SeedData.Permissions.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Require(permissionNames.Count == SeedData.Permissions.Count, "Seed permission names must be unique.");

        var rolePermissions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [SeedData.AdminRoleName] = permissionNames,
            [SeedData.OperatorRoleName] = SeedData.OperatorPermissions.ToHashSet(StringComparer.Ordinal),
            [SeedData.ViewerRoleName] = new(StringComparer.Ordinal),
        };

        foreach (var role in DemoData.Roles)
        {
            Require(!rolePermissions.ContainsKey(role.Name), $"Duplicate demo role '{role.Name}'.");
            Require(role.Permissions.All(permissionNames.Contains),
                $"Role '{role.Name}' refers to an unknown permission.");
            RequireUnique(role.Permissions, $"permissions on role '{role.Name}'");
            rolePermissions[role.Name] = role.Permissions.ToHashSet(StringComparer.Ordinal);
            if (role.AreaWkt is not null)
                Parse(role.AreaWkt, OgcGeometryType.Polygon, $"area of role '{role.Name}'");
        }

        Require(rolePermissions.Count == 5, $"Expected five roles, found {rolePermissions.Count}.");
        Require(rolePermissions[SeedData.ViewerRoleName].Count == 0,
            "Viewer must remain permission-free.");
        Require(rolePermissions[SeedData.OperatorRoleName].SetEquals(["add_poi"]),
            "Operator must inherit add_poi and no redundant drawing permission.");

        var users = DemoData.Users.ToDictionary(u => u.Username, StringComparer.Ordinal);
        var effectiveAreas = new Dictionary<string, Geometry?>(StringComparer.Ordinal);
        foreach (var user in DemoData.Users)
        {
            Require(rolePermissions.TryGetValue(user.Role, out var inherited),
                $"User '{user.Username}' refers to unknown role '{user.Role}'.");
            RequireUnique(user.DirectPermissions, $"direct permissions on '{user.Username}'");
            Require(user.DirectPermissions.All(permissionNames.Contains),
                $"User '{user.Username}' refers to an unknown direct permission.");

            var duplicateGrants = user.DirectPermissions.Where(inherited!.Contains).ToArray();
            Require(duplicateGrants.Length == 0,
                $"User '{user.Username}' duplicates role permission(s) directly: {string.Join(", ", duplicateGrants)}.");

            effectiveAreas[user.Username] = user.AreaWkt is not null
                ? Parse(user.AreaWkt, OgcGeometryType.Polygon, $"area of user '{user.Username}'")
                : DemoData.Roles.FirstOrDefault(r => r.Name == user.Role)?.AreaWkt is { } roleArea
                    ? Parse(roleArea, OgcGeometryType.Polygon, $"effective area of '{user.Username}'")
                    : null;
        }

        Require(DemoData.Users.Count(u => u.AreaWkt is not null)
                + DemoData.Roles.Count(r => r.AreaWkt is not null)
                == DemoData.ExpectedAreaCount,
            $"Expected {DemoData.ExpectedAreaCount} authorization areas.");

        Require(DemoData.Provinces.Count == DemoData.ExpectedProvinceCount,
            $"Expected {DemoData.ExpectedProvinceCount} provinces, found {DemoData.Provinces.Count}.");
        RequireUnique(DemoData.Provinces.Select(p => p.Name), "province names");
        var expectedRegionCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Marmara"] = 11,
            ["Aegean"] = 8,
            ["Mediterranean"] = 8,
            ["Central Anatolia"] = 13,
            ["Black Sea"] = 18,
            ["Eastern Anatolia"] = 14,
            ["Southeastern Anatolia"] = 9,
        };
        var actualRegionCounts = DemoData.Provinces
            .GroupBy(p => p.Region)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        Require(actualRegionCounts.Count == expectedRegionCounts.Count
                && expectedRegionCounts.All(pair =>
                    actualRegionCounts.TryGetValue(pair.Key, out var count) && count == pair.Value),
            "Province-to-region manifest does not match Turkey's seven-region 81-province distribution.");

        Require(DemoData.Shapes.Count + DemoData.Provinces.Count == DemoData.ExpectedShapeCount,
            $"Expected {DemoData.ExpectedShapeCount} total shapes.");
        RequireUnique(DemoData.Shapes.Select(s => s.Name), "explicit shape names");
        var expectedShapeOwners = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["admin"] = 19,
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
        var actualShapeOwners = DemoData.Shapes
            .GroupBy(s => s.Owner)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        Require(expectedShapeOwners.Count == actualShapeOwners.Count
                && expectedShapeOwners.All(pair =>
                    actualShapeOwners.TryGetValue(pair.Key, out var count) && count == pair.Value),
            "Explicit shape ownership no longer matches the 83-row contract.");
        Require(DemoData.Shapes.Count(s => s.Type == "point") + DemoData.Provinces.Count == 109
                && DemoData.Shapes.Count(s => s.Type == "line") == 30
                && DemoData.Shapes.Count(s => s.Type == "polygon") == 25,
            "Shape type distribution must be 109 points, 30 lines and 25 polygons.");

        foreach (var shape in DemoData.Shapes)
        {
            Require(users.TryGetValue(shape.Owner, out var owner),
                $"Shape '{shape.Name}' refers to unknown owner '{shape.Owner}'.");
            var expectedType = shape.Type switch
            {
                "point" => OgcGeometryType.Point,
                "line" => OgcGeometryType.LineString,
                "polygon" => OgcGeometryType.Polygon,
                _ => throw new InvalidOperationException(
                    $"Shape '{shape.Name}' has unknown type '{shape.Type}'."),
            };
            var geometry = Parse(shape.Wkt, expectedType, shape.Name);
            Require(shape.DaysAgo >= 0, $"Shape '{shape.Name}' has a negative age.");

            var effectivePermissions = rolePermissions[owner!.Role]
                .Concat(owner.DirectPermissions)
                .ToHashSet(StringComparer.Ordinal);
            if (!string.Equals(owner.Username, "viewer", StringComparison.Ordinal))
            {
                Require(effectivePermissions.Contains($"add_{shape.Type}"),
                    $"Owner '{owner.Username}' cannot manage seeded {shape.Type} '{shape.Name}'.");
            }

            if (effectiveAreas[owner.Username] is { } area)
                Require(area.Covers(geometry),
                    $"Restricted shape '{shape.Name}' falls outside '{owner.Username}' effective area.");
        }

        Require(DemoData.Categories.Count == DemoData.ExpectedCategoryCount,
            $"Expected {DemoData.ExpectedCategoryCount} categories, found {DemoData.Categories.Count}.");
        RequireUnique(DemoData.Categories.Select(c => c.Name), "category names");
        var categories = DemoData.Categories.ToDictionary(c => c.Name, StringComparer.Ordinal);
        foreach (var category in DemoData.Categories)
        {
            Require(category.Parent is null || categories.ContainsKey(category.Parent),
                $"Category '{category.Name}' has unknown parent '{category.Parent}'.");
            Require(PoiIconCatalog.TryNormalize(category.IconKey, out var normalized)
                    && normalized == category.IconKey,
                $"Category '{category.Name}' has invalid or non-canonical icon '{category.IconKey}'.");

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var cursor = category;
            string? effectiveIcon = null;
            while (true)
            {
                Require(visited.Add(cursor.Name), $"Category cycle detected at '{cursor.Name}'.");
                effectiveIcon ??= cursor.IconKey;
                if (cursor.Parent is null)
                    break;
                cursor = categories[cursor.Parent];
            }

            Require(PoiIconCatalog.TryNormalize(effectiveIcon, out var inheritedIcon),
                $"Category '{category.Name}' does not resolve to a valid inherited icon.");
            _ = inheritedIcon ?? PoiIconCatalog.DefaultIconKey;
        }

        var parentNames = DemoData.Categories
            .Where(c => c.Parent is not null)
            .Select(c => c.Parent!)
            .ToHashSet(StringComparer.Ordinal);
        var leaves = DemoData.Categories
            .Where(c => !parentNames.Contains(c.Name))
            .Select(c => c.Name)
            .ToHashSet(StringComparer.Ordinal);
        Require(DemoData.Categories.Count(c => c.Parent is null) == 9 && leaves.Count == 33,
            "Category tree must contain nine roots and 33 leaf categories.");

        Require(DemoData.Pois.Count + DemoData.Provinces.Count == DemoData.ExpectedPoiCount,
            $"Expected {DemoData.ExpectedPoiCount} total POIs.");
        RequireUnique(DemoData.Pois.Select(p => p.Name), "curated POI names");
        RequireUnique(DemoData.ProvincePoiTemplates.Select(t => t.NameSuffix),
            "province POI template suffixes");
        foreach (var template in DemoData.ProvincePoiTemplates)
        {
            Require(leaves.Contains(template.Category),
                $"Province POI template '{template.NameSuffix}' must use a leaf category.");
            Require(template.Close >= template.Open,
                $"Province POI template '{template.NameSuffix}' has invalid sample hours.");
        }

        // Expected per-category use derives from the manifest itself: curated entries plus the
        // template rotation applied over the 81 provinces. Every leaf must appear on the map.
        var expectedCategoryUse = ExpectedPoiCategoryUse();
        Require(expectedCategoryUse.Keys.All(leaves.Contains),
            "Every curated POI must use a leaf category.");
        foreach (var leaf in leaves)
            Require(expectedCategoryUse.GetValueOrDefault(leaf) > 0,
                $"Leaf category '{leaf}' is never used by a curated or generated POI.");

        foreach (var poi in DemoData.Pois)
        {
            Require(users.TryGetValue(poi.Owner, out var owner),
                $"POI '{poi.Name}' refers to unknown owner '{poi.Owner}'.");
            Require(categories.ContainsKey(poi.Category),
                $"POI '{poi.Name}' refers to unknown category '{poi.Category}'.");
            Require(poi.DaysAgo >= 0 && poi.Close >= poi.Open,
                $"POI '{poi.Name}' has invalid sample hours or age.");
            var geometry = Parse(poi.Wkt, OgcGeometryType.Point, poi.Name);
            var effectivePermissions = rolePermissions[owner!.Role]
                .Concat(owner.DirectPermissions)
                .ToHashSet(StringComparer.Ordinal);
            Require(effectivePermissions.Contains("add_poi"),
                $"Owner '{owner.Username}' cannot create curated POI '{poi.Name}'.");
            if (effectiveAreas[owner.Username] is { } area)
                Require(area.Covers(geometry),
                    $"Restricted POI '{poi.Name}' falls outside '{owner.Username}' effective area.");
        }

        var poiOwners = DemoData.Pois
            .GroupBy(p => p.Owner)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        Require(poiOwners.Count == 5
                && poiOwners.GetValueOrDefault("admin") == 25
                && poiOwners.GetValueOrDefault("istanbul_operator") == 12
                && new[] { "antalya_operator", "gaziantep_operator", "trabzon_operator" }
                    .All(owner => poiOwners.GetValueOrDefault(owner) == 7),
            "Curated POI ownership must be admin 25, istanbul_operator 12, and seven for the other three operators.");
    }

    // Manifest-derived per-category POI counts: curated entries plus the province template rotation.
    // Shared by ValidateManifest and VerifyAsync so the expectation cannot drift from the generator.
    public static Dictionary<string, int> ExpectedPoiCategoryUse()
    {
        var counts = DemoData.Pois
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        for (var index = 0; index < DemoData.ExpectedProvinceCount; index++)
        {
            var category = DemoData.ProvincePoiTemplates[index % DemoData.ProvincePoiTemplates.Count].Category;
            counts[category] = counts.GetValueOrDefault(category) + 1;
        }
        return counts;
    }

    private static async Task ValidateProvinceSourceManifestAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "provinces.geojson");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        if (!doc.RootElement.TryGetProperty("features", out var features)
            || features.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("provinces.geojson is not a GeoJSON FeatureCollection.");
        }

        var sourceNames = features.EnumerateArray()
            .Select(feature => feature.GetProperty("properties").GetProperty("name").GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

        RequireExactNames(
            "province manifest and provinces.geojson",
            sourceNames,
            DemoData.Provinces.Select(p => p.Name));
    }

    private static async Task<IReadOnlyList<ProvinceSeedPoint>> LoadProvinceSeedPointsAsync(AppDbContext db)
    {
        var rows = await db.Provinces.AsNoTracking().ToListAsync();
        RequireExactNames(
            "province manifest and tbl_province",
            rows.Select(p => p.Name),
            DemoData.Provinces.Select(p => p.Name));

        var byName = rows.ToDictionary(p => p.Name, StringComparer.Ordinal);
        return DemoData.Provinces.Select((manifest, index) =>
        {
            var boundary = byName[manifest.Name].Geom;
            var (marker, hub) = DeriveProvincePoints(boundary, index, manifest.Name);
            return new ProvinceSeedPoint(manifest, boundary, marker, hub);
        }).ToList();
    }

    // Pure deterministic geometry rule shared with offline tests: the marker uses InteriorPoint,
    // then the hub moves in a province/index-specific direction within the marker's guaranteed
    // interior-clearance radius. No database state is needed to prove both points are distinct and
    // covered by each of the 81 province geometries.
    public static (Point Marker, Point Hub) DeriveProvincePoints(
        Geometry boundary,
        int provinceIndex,
        string provinceName)
    {
        Require(!boundary.IsEmpty && boundary.IsValid,
            $"Province '{provinceName}' has an empty or invalid boundary.");

        var srid = boundary.SRID == 0 ? Srid : boundary.SRID;
        var marker = boundary.InteriorPoint;
        marker.SRID = srid;
        Require(boundary.Covers(marker),
            $"Could not derive a covered marker for province '{provinceName}'.");

        var clearance = marker.Distance(boundary.Boundary);
        Require(clearance > 1e-10,
            $"Province '{provinceName}' has no usable interior clearance for two demo points.");

        Point? hub = null;
        var angle = (provinceIndex + 1) * 2.399963229728653;
        var distance = clearance * 0.35;
        for (var attempt = 0; attempt < 12 && hub is null; attempt++, distance /= 2)
        {
            var candidate = boundary.Factory.CreatePoint(new Coordinate(
                marker.X + Math.Cos(angle) * distance,
                marker.Y + Math.Sin(angle) * distance));
            candidate.SRID = srid;
            if (boundary.Covers(candidate) && candidate.Distance(marker) > 1e-12)
                hub = candidate;
        }

        Require(hub is not null,
            $"Could not derive a distinct covered POI point for province '{provinceName}'.");
        return (marker, hub!);
    }

    private static void RequireUnique(IEnumerable<string> values, string what)
    {
        var duplicates = values
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Require(duplicates.Length == 0,
            $"Duplicate {what}: {string.Join(", ", duplicates)}.");
    }

    private static void RequireExactNames(
        string what,
        IEnumerable<string> actualValues,
        IEnumerable<string> expectedValues)
    {
        var actual = actualValues.ToList();
        var expected = expectedValues.ToList();
        RequireUnique(actual, $"{what} names");
        var missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
        var unexpected = actual.Except(expected, StringComparer.Ordinal).ToArray();
        Require(actual.Count == expected.Count && missing.Length == 0 && unexpected.Length == 0,
            $"{what} differ. Missing: [{string.Join(", ", missing)}]. " +
            $"Unexpected: [{string.Join(", ", unexpected)}].");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    // --- Wipe --------------------------------------------------------------------------------------

    private static async Task WipeAsync(AppDbContext db)
    {
        // TRUNCATE, not ExecuteDelete: ExecuteDelete honours the global query filters, so it would
        // walk straight past soft-deleted users and inactive shapes; and modified_user_id -> users
        // is NO ACTION (only user_id cascades), so a plain DELETE FROM users fails outright while
        // any shape still names a user as its last modifier.
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE " + string.Join(", ", Tables) + ";");

        var restarts = Sequences.Select(s => $"ALTER SEQUENCE {s} RESTART WITH 1;");
        await db.Database.ExecuteSqlRawAsync(string.Join("\n", restarts));
    }

    // --- Inserts -----------------------------------------------------------------------------------
    //
    // Ids come from nextval('seq_...'), so EF gets them back from the INSERT and populates each
    // entity in place. That is what every FK below is built from — hence one SaveChanges per wave.

    private static async Task<Dictionary<string, int>> InsertUsersAsync(AppDbContext db)
    {
        var joined = DateTime.UtcNow.AddDays(-120);

        var users = DemoData.Users.Select((u, i) => new User
        {
            Username = u.Username,
            PasswordHash = BC.HashPassword(DemoData.Password),
            CreatedAt = joined.AddDays(i * 4),
        }).ToList();

        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        return users.ToDictionary(u => u.Username, u => u.Id);
    }

    private static async Task<Dictionary<string, int>> InsertRolesAsync(AppDbContext db)
    {
        db.Roles.AddRange(DemoData.Roles.Select(r => new Role
        {
            Name = r.Name,
            Description = r.Description,
            CreatedAt = DateTime.UtcNow.AddDays(-115),
        }));
        await db.SaveChangesAsync();

        // Includes Admin/Operator/Viewer from AdminSeeder, which the personas also reference.
        return await db.Roles.ToDictionaryAsync(r => r.Name, r => r.Id);
    }

    private static async Task InsertGrantsAsync(
        AppDbContext db,
        Dictionary<string, int> users,
        Dictionary<string, int> roles,
        Dictionary<string, int> permissions)
    {
        // Only the scenario's own roles: Admin, Operator and Viewer already hold exactly what
        // AdminSeeder gave them.
        db.RolePermissions.AddRange(
            from role in DemoData.Roles
            from permission in role.Permissions
            select new RolePermission { RoleId = roles[role.Name], PermissionId = permissions[permission] });

        db.UserRoles.AddRange(DemoData.Users.Select(u => new UserRole
        {
            UserId = users[u.Username],
            RoleId = roles[u.Role],
        }));

        db.UserPermissions.AddRange(
            from user in DemoData.Users
            from permission in user.DirectPermissions
            select new UserPermission { UserId = users[user.Username], PermissionId = permissions[permission] });

        await db.SaveChangesAsync();
    }

    private static async Task InsertAreasAsync(
        AppDbContext db, Dictionary<string, int> users, Dictionary<string, int> roles)
    {
        db.GeoAuthorizations.AddRange(DemoData.Roles
            .Where(r => r.AreaWkt is not null)
            .Select(r => new GeoAuthorization
            {
                RoleId = roles[r.Name],
                Geom = Parse(r.AreaWkt!, OgcGeometryType.Polygon, $"area of role '{r.Name}'"),
            }));

        db.GeoAuthorizations.AddRange(DemoData.Users
            .Where(u => u.AreaWkt is not null)
            .Select(u => new GeoAuthorization
            {
                UserId = users[u.Username],
                Geom = Parse(u.AreaWkt!, OgcGeometryType.Polygon, $"area of user '{u.Username}'"),
            }));

        await db.SaveChangesAsync();
    }

    private static async Task<Dictionary<string, int>> InsertCategoriesAsync(AppDbContext db, int adminId)
    {
        var ids = new Dictionary<string, int>();
        var pending = DemoData.Categories.ToList();
        var createdAt = DateTime.UtcNow.AddDays(-100);

        // One SaveChanges per level of the tree: PoiCategory exposes no navigation property, only a
        // raw ParentId, so EF cannot fix a parent's generated id into its children for us. A child
        // saved alongside its parent would write ParentId = 0 and the Restrict FK would reject it.
        while (pending.Count > 0)
        {
            var level = pending
                .Where(c => c.Parent is null || ids.ContainsKey(c.Parent))
                .ToList();

            if (level.Count == 0)
                throw new InvalidOperationException("POI category tree has a parent that does not exist.");

            var rows = level.Select(c => new PoiCategory
            {
                Name = c.Name,
                ParentId = c.Parent is null ? null : ids[c.Parent],
                Color = c.Color,
                IconKey = c.IconKey,
                UserId = adminId,
                ModifiedUserId = adminId,
                CreatedAt = createdAt,
            }).ToList();

            db.PoiCategories.AddRange(rows);
            await db.SaveChangesAsync();

            foreach (var (category, row) in level.Zip(rows))
                ids[category.Name] = row.Id;

            pending = pending.Except(level).ToList();
        }

        return ids;
    }

    private static async Task InsertShapesAsync(
        AppDbContext db,
        Dictionary<string, int> users,
        IReadOnlyList<ProvinceSeedPoint> provinces)
    {
        var adminId = users["admin"];
        db.Points.AddRange(provinces.Select((province, index) => new PointFeature
        {
            UserId = adminId,
            Name = $"{province.Manifest.Name} Province Marker",
            Color = province.Manifest.Color,
            Geom = province.Marker,
            CreatedAt = DateTime.UtcNow.AddDays(-(100 - index % 40)),
            ModifiedUserId = adminId,
        }));

        foreach (var shape in DemoData.Shapes)
        {
            var ownerId = users[shape.Owner];
            // Not stamped by SaveChanges (unlike ModifiedDate), so it is ours to backdate.
            var createdAt = DateTime.UtcNow.AddDays(-shape.DaysAgo);

            switch (shape.Type)
            {
                case "point":
                    db.Points.Add(new PointFeature
                    {
                        UserId = ownerId,
                        Name = shape.Name,
                        Color = shape.Color,
                        Geom = Parse(shape.Wkt, OgcGeometryType.Point, shape.Name),
                        CreatedAt = createdAt,
                        // A never-edited shape reports its creator as its last modifier, which is
                        // what the services do for a freshly drawn one.
                        ModifiedUserId = ownerId,
                    });
                    break;

                case "line":
                    db.Lines.Add(new LineFeature
                    {
                        UserId = ownerId,
                        Name = shape.Name,
                        Color = shape.Color,
                        Geom = Parse(shape.Wkt, OgcGeometryType.LineString, shape.Name),
                        CreatedAt = createdAt,
                        ModifiedUserId = ownerId,
                    });
                    break;

                case "polygon":
                    db.Polygons.Add(new PolygonFeature
                    {
                        UserId = ownerId,
                        Name = shape.Name,
                        Color = shape.Color,
                        Geom = Parse(shape.Wkt, OgcGeometryType.Polygon, shape.Name),
                        CreatedAt = createdAt,
                        ModifiedUserId = ownerId,
                    });
                    break;

                default:
                    throw new InvalidOperationException($"'{shape.Name}' has unknown shape type '{shape.Type}'.");
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task InsertPoisAsync(
        AppDbContext db,
        Dictionary<string, int> users,
        Dictionary<string, int> categories,
        IReadOnlyList<ProvinceSeedPoint> provinces)
    {
        var adminId = users["admin"];
        db.Pois.AddRange(provinces.Select((province, index) =>
        {
            var (name, category, open, close) = DemoData.ProvincePoiFor(index, province.Manifest.Name);
            return new Poi
            {
                UserId = adminId,
                Name = name,
                CategoryId = categories[category],
                Geom = province.Hub,
                OpenTime = open,
                CloseTime = close,
                CreatedAt = DateTime.UtcNow.AddDays(-(90 - index % 30)),
                ModifiedUserId = adminId,
            };
        }));

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

    // --- After the inserts -------------------------------------------------------------------------

    private static async Task BackdateModifiedDatesAsync(AppDbContext db)
    {
        // AppDbContext.SaveChanges stamps ModifiedDate with UtcNow on every insert, so seeded rows
        // would all read "created three months ago, last modified two seconds ago". Raw SQL is the
        // only way past it, and modified_date = created_at is exactly what the services mean by a
        // row nobody has edited.
        var updates = Tables.Select(t => $"UPDATE {t} SET modified_date = created_at;");
        await db.Database.ExecuteSqlRawAsync(string.Join("\n", updates));
    }

    // The failure this guards against is quiet and nasty: the authorization area is checked when a
    // shape is created or moved but never when it is read, so a seeded shape outside its owner's
    // area shows up on the map perfectly and then refuses every edit with a 403. Better to fail the
    // seed than to find that out during the demo.
    private static async Task AssertEveryShapeIsInsideItsAreaAsync(AppDbContext db)
    {
        var areas = await db.GeoAuthorizations.ToListAsync();
        var userRoles = await db.UserRoles.ToListAsync();
        var usernames = await db.Users.ToDictionaryAsync(u => u.Id, u => u.Username);

        Geometry? EffectiveArea(int userId)
        {
            // Mirrors GeoAuthorizationService.GetEffectiveAreaAsync: the user's own area overrides
            // their roles' areas outright; otherwise the roles' areas are unioned; no area at all
            // means unrestricted.
            var own = areas.FirstOrDefault(a => a.UserId == userId);
            if (own is not null)
                return own.Geom;

            var roleIds = userRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToHashSet();

            return areas
                .Where(a => a.RoleId is not null && roleIds.Contains(a.RoleId.Value))
                .Select(a => a.Geom)
                .Aggregate((Geometry?)null, (acc, geom) => acc is null ? geom : acc.Union(geom));
        }

        var owned = new List<(int UserId, string Kind, string? Name, Geometry Geom)>();
        owned.AddRange((await db.Points.ToListAsync()).Select(p => (p.UserId, "point", p.Name, p.Geom)));
        owned.AddRange((await db.Lines.ToListAsync()).Select(l => (l.UserId, "line", l.Name, l.Geom)));
        owned.AddRange((await db.Polygons.ToListAsync()).Select(g => (g.UserId, "polygon", g.Name, g.Geom)));
        owned.AddRange((await db.Pois.ToListAsync()).Select(p => (p.UserId, "poi", p.Name, p.Geom)));

        var escaped = owned
            .Where(o => EffectiveArea(o.UserId) is { } area && !area.Covers(o.Geom))
            .Select(o => $"{o.Kind} '{o.Name}' (owner {usernames[o.UserId]})")
            .ToList();

        if (escaped.Count > 0)
            throw new InvalidOperationException(
                "These seeded shapes fall outside their owner's authorized area, so they would be " +
                "visible but not editable: " + string.Join("; ", escaped));
    }

    // Verifies the database state produced inside the still-open transaction. This deliberately
    // duplicates the externally visible headline counts: if a future insert path stops honoring the
    // manifest, seed-demo rolls back instead of publishing a subtly broken demonstration database.
    private static async Task AssertDemoContractAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, int> userIds,
        IReadOnlyList<ProvinceSeedPoint> provinces)
    {
        var orderedUsers = await db.Users
            .OrderBy(user => user.Id)
            .Select(user => new { user.Id, user.Username })
            .ToListAsync();
        Require(orderedUsers.Count == DemoData.ExpectedUserCount,
            $"Post-insert user count is {orderedUsers.Count}, expected {DemoData.ExpectedUserCount}.");
        Require(orderedUsers.Select(user => user.Id).SequenceEqual(Enumerable.Range(1, 17))
                && orderedUsers.Select(user => user.Username)
                    .SequenceEqual(DemoData.Users.Select(user => user.Username)),
            "Post-insert account ids/order do not match the deterministic 1..17 contract.");
        Require(userIds.Count == orderedUsers.Count
                && orderedUsers.All(user => userIds[user.Username] == user.Id),
            "Inserted user-id lookup differs from persisted account order.");

        Require(await db.Roles.CountAsync() == 5, "Post-insert role count must be exactly five.");
        Require(await db.Permissions.CountAsync() == SeedData.Permissions.Count,
            "Post-insert permission catalogue count differs from SeedData.");
        Require(await db.UserRoles.CountAsync() == DemoData.ExpectedUserCount,
            "Every demo account must have exactly one role.");
        Require(await db.GeoAuthorizations.CountAsync() == DemoData.ExpectedAreaCount,
            $"Post-insert authorization-area count must be {DemoData.ExpectedAreaCount}.");
        Require(await db.PoiCategories.CountAsync() == DemoData.ExpectedCategoryCount,
            $"Post-insert category count must be {DemoData.ExpectedCategoryCount}.");
        Require(await db.Provinces.CountAsync() == DemoData.ExpectedProvinceCount,
            $"Post-insert province count must be {DemoData.ExpectedProvinceCount}.");

        var pointCount = await db.Points.CountAsync();
        var lineCount = await db.Lines.CountAsync();
        var polygonCount = await db.Polygons.CountAsync();
        Require(pointCount == 109 && lineCount == 30 && polygonCount == 25,
            $"Post-insert shape types are {pointCount} points/{lineCount} lines/{polygonCount} polygons; " +
            "expected 109/30/25.");
        Require(pointCount + lineCount + polygonCount == DemoData.ExpectedShapeCount,
            $"Post-insert total shape count must be {DemoData.ExpectedShapeCount}.");
        Require(await db.Pois.CountAsync() == DemoData.ExpectedPoiCount,
            $"Post-insert POI count must be {DemoData.ExpectedPoiCount}.");

        var shapeOwners = new Dictionary<int, int>();
        static void AddOwnerCounts(
            Dictionary<int, int> target,
            IEnumerable<KeyValuePair<int, int>> additions)
        {
            foreach (var (ownerId, count) in additions)
                target[ownerId] = target.GetValueOrDefault(ownerId) + count;
        }

        AddOwnerCounts(shapeOwners, (await db.Points
                .GroupBy(row => row.UserId)
                .Select(group => new { OwnerId = group.Key, Count = group.Count() })
                .ToListAsync())
            .Select(row => KeyValuePair.Create(row.OwnerId, row.Count)));
        AddOwnerCounts(shapeOwners, (await db.Lines
                .GroupBy(row => row.UserId)
                .Select(group => new { OwnerId = group.Key, Count = group.Count() })
                .ToListAsync())
            .Select(row => KeyValuePair.Create(row.OwnerId, row.Count)));
        AddOwnerCounts(shapeOwners, (await db.Polygons
                .GroupBy(row => row.UserId)
                .Select(group => new { OwnerId = group.Key, Count = group.Count() })
                .ToListAsync())
            .Select(row => KeyValuePair.Create(row.OwnerId, row.Count)));

        Require(shapeOwners.GetValueOrDefault(userIds["admin"]) == 100,
            "Admin must own exactly 100 private shapes.");
        foreach (var manager in DemoData.Users.Where(user => user.Role == DemoData.RegionalManagerRoleName))
            Require(shapeOwners.GetValueOrDefault(userIds[manager.Username]) == 7,
                $"{manager.Username} must own exactly seven shapes.");
        Require(shapeOwners.GetValueOrDefault(userIds["ankara_editor"]) == 6,
            "ankara_editor must own exactly six shapes.");
        foreach (var editor in new[] { "istanbul_editor", "izmir_editor", "antalya_editor" })
            Require(shapeOwners.GetValueOrDefault(userIds[editor]) == 2,
                $"{editor} must own exactly two shapes.");
        Require(shapeOwners.GetValueOrDefault(userIds["viewer"]) == 3,
            "Viewer must retain exactly three read-only legacy shapes.");

        var poiOwners = await db.Pois
            .GroupBy(row => row.UserId)
            .Select(group => new { OwnerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.OwnerId, row => row.Count);
        Require(poiOwners.Count == 5 && poiOwners.GetValueOrDefault(userIds["admin"]) == 106,
            "POI ownership must include admin with exactly 106 records (81 generated + 25 curated).");
        Require(poiOwners.GetValueOrDefault(userIds["istanbul_operator"]) == 12,
            "istanbul_operator must own exactly twelve POIs.");
        foreach (var poiOperator in new[] { "antalya_operator", "gaziantep_operator", "trabzon_operator" })
        {
            Require(poiOwners.GetValueOrDefault(userIds[poiOperator]) == 7,
                $"{poiOperator} must own exactly seven POIs.");
        }

        var persistedPoints = await db.Points
            .Where(point => point.UserId == userIds["admin"])
            .ToListAsync();
        var persistedPois = await db.Pois.ToListAsync();
        for (var provinceIndex = 0; provinceIndex < provinces.Count; provinceIndex++)
        {
            var province = provinces[provinceIndex];
            var markerName = $"{province.Manifest.Name} Province Marker";
            var markerMatches = persistedPoints.Where(point => point.Name == markerName).ToList();
            Require(markerMatches.Count == 1 && province.Boundary.Covers(markerMatches[0].Geom),
                $"Province '{province.Manifest.Name}' must have one covered admin marker.");

            var generated = DemoData.ProvincePoiFor(provinceIndex, province.Manifest.Name);
            var generatedMatches = persistedPois.Where(poi => poi.Name == generated.Name).ToList();
            Require(generatedMatches.Count == 1
                    && generatedMatches[0].UserId == userIds["admin"]
                    && province.Boundary.Covers(generatedMatches[0].Geom),
                $"Province '{province.Manifest.Name}' must have one covered admin '{generated.Name}' POI.");
            Require(markerMatches[0].Geom.Distance(generatedMatches[0].Geom) > 1e-12,
                $"Province '{province.Manifest.Name}' marker and generated POI must not overlap.");
        }

        var categories = await db.PoiCategories.ToDictionaryAsync(category => category.Id);
        var categoriesByName = categories.Values.ToDictionary(category => category.Name, StringComparer.Ordinal);
        RequireExactNames(
            "persisted categories and DemoData.Categories",
            categoriesByName.Keys,
            DemoData.Categories.Select(category => category.Name));
        foreach (var manifestCategory in DemoData.Categories)
        {
            var persisted = categoriesByName[manifestCategory.Name];
            var persistedParentName = persisted.ParentId is null
                ? null
                : categories[persisted.ParentId.Value].Name;
            Require(persistedParentName == manifestCategory.Parent
                    && persisted.Color == manifestCategory.Color
                    && persisted.IconKey == manifestCategory.IconKey,
                $"Persisted category '{manifestCategory.Name}' differs from its parent/color/icon manifest.");
        }

        var poiCategoryCounts = await db.Pois
            .GroupBy(poi => poi.CategoryId)
            .Select(group => new { CategoryId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.CategoryId, row => row.Count);
        var expectedCategoryUse = ExpectedPoiCategoryUse();
        foreach (var manifestCategory in DemoData.Categories)
        {
            var expectedUse = expectedCategoryUse.GetValueOrDefault(manifestCategory.Name);
            Require(poiCategoryCounts.GetValueOrDefault(categoriesByName[manifestCategory.Name].Id)
                    == expectedUse,
                $"Persisted category '{manifestCategory.Name}' should own {expectedUse} POI(s).");
        }

        foreach (var category in categories.Values)
        {
            var visited = new HashSet<int>();
            var cursor = category;
            string? effectiveIcon = null;
            while (true)
            {
                Require(visited.Add(cursor.Id),
                    $"Persisted category cycle detected at '{cursor.Name}'.");
                if (cursor.IconKey is not null)
                {
                    Require(PoiIconCatalog.TryNormalize(cursor.IconKey, out var normalized)
                            && normalized == cursor.IconKey,
                        $"Persisted category '{cursor.Name}' has invalid icon '{cursor.IconKey}'.");
                    effectiveIcon ??= cursor.IconKey;
                }

                if (cursor.ParentId is null)
                    break;
                Require(categories.TryGetValue(cursor.ParentId.Value, out var parent),
                    $"Persisted category '{cursor.Name}' has unresolved parent id {cursor.ParentId}.");
                cursor = parent!;
            }

            var resolved = effectiveIcon ?? PoiIconCatalog.DefaultIconKey;
            Require(PoiIconCatalog.TryNormalize(resolved, out var normalizedResolved)
                    && normalizedResolved is not null,
                $"Persisted category '{category.Name}' has no valid effective icon.");
        }

        var userRoles = await db.UserRoles.ToListAsync();
        var rolePermissions = await db.RolePermissions.ToListAsync();
        var directPermissions = await db.UserPermissions.ToListAsync();
        var rolesById = await db.Roles.ToDictionaryAsync(role => role.Id);
        var permissionsById = await db.Permissions.ToDictionaryAsync(permission => permission.Id);
        var expectedPermissionsByRole = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [SeedData.AdminRoleName] = SeedData.Permissions
                .Select(permission => permission.Name)
                .ToHashSet(StringComparer.Ordinal),
            [DemoData.RegionalManagerRoleName] = ["add_point", "add_line", "add_polygon"],
            [DemoData.EditorRoleName] = ["add_point", "add_line"],
            [SeedData.OperatorRoleName] = ["add_poi"],
            [SeedData.ViewerRoleName] = [],
        };
        foreach (var role in rolesById.Values)
        {
            var actual = rolePermissions
                .Where(grant => grant.RoleId == role.Id)
                .Select(grant => permissionsById[grant.PermissionId].Name)
                .ToHashSet(StringComparer.Ordinal);
            Require(expectedPermissionsByRole.TryGetValue(role.Name, out var expected)
                    && actual.SetEquals(expected),
                $"Persisted permission matrix differs for role '{role.Name}'.");
        }

        Require(directPermissions.Count == 1
                && directPermissions[0].UserId == userIds["ankara_editor"]
                && permissionsById[directPermissions[0].PermissionId].Name == "add_polygon",
            "ankara_editor must be the only direct grant holder, with add_polygon only.");

        var inheritedPairs =
            (from userRole in userRoles
             join rolePermission in rolePermissions on userRole.RoleId equals rolePermission.RoleId
             select (userRole.UserId, rolePermission.PermissionId))
            .ToHashSet();
        var duplicatedPairs = directPermissions
            .Where(direct => inheritedPairs.Contains((direct.UserId, direct.PermissionId)))
            .ToList();
        Require(duplicatedPairs.Count == 0,
            "A permission was granted both directly and through a role after insertion.");
    }

    // --- Logging -----------------------------------------------------------------------------------

    private static async Task LogCurrentContentsAsync(AppDbContext db, ILogger logger)
    {
        // IgnoreQueryFilters so soft-deleted rows are counted too — they are about to go as well.
        logger.LogWarning(
            "Currently: {Users} users, {Roles} roles, {Shapes} shapes, {Pois} POIs, {Categories} POI categories.",
            await db.Users.IgnoreQueryFilters().CountAsync(),
            await db.Roles.IgnoreQueryFilters().CountAsync(),
            await db.Points.IgnoreQueryFilters().CountAsync()
                + await db.Lines.IgnoreQueryFilters().CountAsync()
                + await db.Polygons.IgnoreQueryFilters().CountAsync(),
            await db.Pois.IgnoreQueryFilters().CountAsync(),
            await db.PoiCategories.IgnoreQueryFilters().CountAsync());
    }

    private static async Task LogSummaryAsync(AppDbContext db, ILogger logger, Dictionary<string, int> users)
    {
        logger.LogInformation(
            "Demo data seeded: {Users} users, {Roles} roles, {Points} points, {Lines} lines, " +
            "{Polygons} polygons, {Areas} authorization areas, {Categories} POI categories, {Pois} POIs.",
            await db.Users.CountAsync(),
            await db.Roles.CountAsync(),
            await db.Points.CountAsync(),
            await db.Lines.CountAsync(),
            await db.Polygons.CountAsync(),
            await db.GeoAuthorizations.CountAsync(),
            await db.PoiCategories.CountAsync(),
            await db.Pois.CountAsync());

        foreach (var persona in DemoData.Users)
            logger.LogInformation(
                "  {Id,2}  {Username,-12} {Role,-15} {Demonstrates}",
                users[persona.Username], persona.Username, persona.Role, persona.Demonstrates);

        logger.LogInformation("All accounts use the password '{Password}'.", DemoData.Password);
        logger.LogWarning(
            "Log out in the browser (clear localStorage) before demoing: ids restart at 1, so a token " +
            "minted before this reseed now points at a different user.");
    }

    // WKTReader hands back geometries with SRID 0; the columns are geometry(<Type>,4326) and PostGIS
    // rejects a mismatch, so every geometry has to be stamped — the same thing the services do.
    private static Geometry Parse(string wkt, OgcGeometryType expected, string what)
    {
        var geom = Reader.Read(wkt);

        if (geom is null || geom.IsEmpty || geom.OgcGeometryType != expected)
            throw new InvalidOperationException($"{what}: expected {expected} WKT, got '{wkt}'.");

        if (!geom.IsValid)
            throw new InvalidOperationException($"{what}: geometry is not valid: '{wkt}'.");

        geom.SRID = Srid;
        return geom;
    }
}
