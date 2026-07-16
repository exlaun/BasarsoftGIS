using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
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
        "tbl_point", "tbl_line", "tbl_polygon",
        "permissions", "roles", "users",
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
    ];

    private const int Srid = 4326;

    private static readonly WKTReader Reader = new();

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

            var permissions = await db.Permissions.ToDictionaryAsync(p => p.Name, p => p.Id);

            var users = await InsertUsersAsync(db);
            var roles = await InsertRolesAsync(db);
            await InsertGrantsAsync(db, users, roles, permissions);
            await InsertAreasAsync(db, users, roles);
            var categories = await InsertCategoriesAsync(db, users["admin"]);
            await InsertShapesAsync(db, users);
            await InsertPoisAsync(db, users, categories);

            await BackdateModifiedDatesAsync(db);
            await AssertEveryShapeIsInsideItsAreaAsync(db);

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

    private static async Task InsertShapesAsync(AppDbContext db, Dictionary<string, int> users)
    {
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
        AppDbContext db, Dictionary<string, int> users, Dictionary<string, int> categories)
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
