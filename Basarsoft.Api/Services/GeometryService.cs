using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace Basarsoft.Api.Services;

public class GeometryService : IGeometryService
{
    private readonly AppDbContext _db;

    private readonly IGeoAuthorizationService _geoAuth;

    // Turns WKT text into a NetTopologySuite geometry. Reusable within this scoped (per-request) service.
    private readonly WKTReader _wktReader = new();

    // The coordinate system we store everything in: EPSG:4326 (WGS84 lon-lat).
    private const int Srid = 4326;

    public GeometryService(AppDbContext db, IGeoAuthorizationService geoAuth)
    {
        _db = db;
        _geoAuth = geoAuth;
    }

    public async Task<GeometryUpdateResult> CreateAsync(string type, GeometryCreateRequest request, int userId)
    {
        // Parse the WKT the client drew. Bad text -> InvalidGeometry -> the controller returns 400.
        Geometry geom;
        try
        {
            geom = _wktReader.Read(request.Wkt);
        }
        catch
        {
            return GeometryUpdateResult.InvalidGeometry;
        }

        // !IsValid rejects e.g. self-intersecting ("bow-tie") polygons: PostGIS would store them but
        // later ST_Within/ST_Intersects over them miscounts or throws. Same guard as GeoAuthorization.
        if (geom is null || geom.IsEmpty || !geom.IsValid)
            return GeometryUpdateResult.InvalidGeometry;

        // The DB columns are typed (geometry(Point,4326) etc.), so the WKT must match the endpoint.
        geom.SRID = Srid;

        // Geographic authorization: a user with an assigned area may only draw fully inside it.
        // Runs in memory on the two NTS geometries — no SQL round-trip.
        if (await _geoAuth.IsOutsideAreaAsync(userId, geom))
            return GeometryUpdateResult.OutsideAuthorizedArea;

        GeometryResponse? saved;
        switch (type.ToLowerInvariant())
        {
            case "point" when geom.OgcGeometryType == OgcGeometryType.Point:
                saved = await AddAsync(_db.Points, geom, request, userId);
                break;
            case "line" when geom.OgcGeometryType == OgcGeometryType.LineString:
                saved = await AddAsync(_db.Lines, geom, request, userId);
                break;
            case "polygon" when geom.OgcGeometryType == OgcGeometryType.Polygon:
                // Count existing rows before inserting this polygon, otherwise the new polygon would
                // count itself. Any inventory that touches or crosses the drawn area counts.
                var intersectingCount = await CountIntersectingAsync(geom, userId);
                saved = await AddAsync(_db.Polygons, geom, request, userId);
                if (saved is not null)
                    saved.IntersectionCount = intersectingCount;
                break;
            default:
                saved = null; // unknown type or WKT geometry doesn't match the table
                break;
        }

        return saved is null ? GeometryUpdateResult.InvalidGeometry : GeometryUpdateResult.Ok(saved);
    }

    public Task<IReadOnlyList<GeometryResponse>> ListAsync(string type, int userId) =>
        type.ToLowerInvariant() switch
        {
            "point" => QueryAsync(_db.Points, userId),
            "line" => QueryAsync(_db.Lines, userId),
            "polygon" => QueryAsync(_db.Polygons, userId),
            _ => Task.FromResult<IReadOnlyList<GeometryResponse>>(Array.Empty<GeometryResponse>()),
        };

    public Task<GeometryUpdateResult> UpdateAsync(string type, int id, GeometryUpdateRequest request, int userId) =>
        type.ToLowerInvariant() switch
        {
            "point" => UpdateEntityAsync(_db.Points, id, request, userId, OgcGeometryType.Point),
            "line" => UpdateEntityAsync(_db.Lines, id, request, userId, OgcGeometryType.LineString),
            "polygon" => UpdateEntityAsync(_db.Polygons, id, request, userId, OgcGeometryType.Polygon),
            _ => Task.FromResult(GeometryUpdateResult.NotFound),
        };

    public Task<DeleteStatus> DeleteAsync(string type, int id, int userId) =>
        type.ToLowerInvariant() switch
        {
            "point" => SoftDeleteAsync(_db.Points, id, userId),
            "line" => SoftDeleteAsync(_db.Lines, id, userId),
            "polygon" => SoftDeleteAsync(_db.Polygons, id, userId),
            _ => Task.FromResult(DeleteStatus.NotFound),
        };

    private async Task<GeometryResponse?> AddAsync<T>(DbSet<T> set, Geometry geom, GeometryCreateRequest request, int userId)
        where T : class, IGeoFeature, new()
    {
        var entity = new T
        {
            UserId = userId,
            Name = request.Name,
            Color = request.Color,
            Geom = geom,
            CreatedAt = DateTime.UtcNow,
            // A never-edited shape reports its creator as the last modifier (same semantics as the
            // modified_date = created_at backfill).
            ModifiedUserId = userId,
        };
        set.Add(entity);
        await _db.SaveChangesAsync();
        return ToResponse(entity);
    }

    // Fetch the caller's rows (the !IsDeleted filter is applied globally) and convert geometry -> WKT.
    private static async Task<IReadOnlyList<GeometryResponse>> QueryAsync<T>(DbSet<T> set, int userId)
        where T : class, IGeoFeature
    {
        var rows = await set.Where(x => x.UserId == userId)
            .OrderBy(x => x.Id)
            .ToListAsync();
        return rows.Select(ToResponse).ToList();
    }

    private async Task<DeleteStatus> SoftDeleteAsync<T>(DbSet<T> set, int id, int userId)
        where T : class, IGeoFeature
    {
        // Ownership check baked in: you can only delete your own shape.
        var entity = await set.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (entity is null)
            return DeleteStatus.NotFound;

        // Removing a shape is bound by the same area as drawing it: an area-restricted caller may
        // not delete a shape that lies outside their boundary.
        if (await _geoAuth.IsOutsideAreaAsync(userId, entity.Geom))
            return DeleteStatus.OutsideAuthorizedArea;

        entity.IsDeleted = true;
        // A soft delete is a modification of the row, so it's audited like any other edit.
        entity.ModifiedUserId = userId;
        await _db.SaveChangesAsync();
        return DeleteStatus.Success;
    }

    // Counts the caller's existing shapes that touch or cross a polygon being saved. The count uses
    // Intersects so an inventory is included even when only part of it overlaps the drawn area.
    private async Task<int> CountIntersectingAsync(Geometry area, int userId)
    {
        var points = await _db.Points.CountAsync(x => x.UserId == userId && x.Geom.Intersects(area));
        var lines = await _db.Lines.CountAsync(x => x.UserId == userId && x.Geom.Intersects(area));
        var polygons = await _db.Polygons.CountAsync(x => x.UserId == userId && x.Geom.Intersects(area));
        return points + lines + polygons;
    }

    // Updates one shape the caller owns. `expectedType` guards the geometry column: a point can't be
    // turned into a polygon, so new WKT of the wrong type is rejected as InvalidGeometry (a 400) rather
    // than being sent to PostGIS to blow up as a 500. Geometry is only touched when WKT was supplied;
    // an attributes-only edit leaves the shape where it is. Ownership is enforced via the WHERE clause.
    private async Task<GeometryUpdateResult> UpdateEntityAsync<T>(
        DbSet<T> set, int id, GeometryUpdateRequest request, int userId, OgcGeometryType expectedType)
        where T : class, IGeoFeature
    {
        var entity = await set.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (entity is null)
            return GeometryUpdateResult.NotFound;

        // You must be allowed to touch the shape where it currently sits. This covers attribute-only
        // edits, which carry no WKT at all, and blocks relocating a shape that already lies outside
        // the caller's area.
        if (await _geoAuth.IsOutsideAreaAsync(userId, entity.Geom))
            return GeometryUpdateResult.OutsideAuthorizedArea;

        if (!string.IsNullOrWhiteSpace(request.Wkt))
        {
            Geometry geom;
            try
            {
                geom = _wktReader.Read(request.Wkt);
            }
            catch
            {
                return GeometryUpdateResult.InvalidGeometry;
            }

            if (geom is null || geom.IsEmpty || !geom.IsValid || geom.OgcGeometryType != expectedType)
                return GeometryUpdateResult.InvalidGeometry;

            geom.SRID = Srid;

            // The destination is checked as well: the shape must end up inside the area, not merely
            // have started there.
            if (await _geoAuth.IsOutsideAreaAsync(userId, geom))
                return GeometryUpdateResult.OutsideAuthorizedArea;

            entity.Geom = geom;
        }

        entity.Name = request.Name;
        entity.Color = request.Color;
        entity.ModifiedUserId = userId;
        // ModifiedDate is stamped automatically by AppDbContext.SaveChanges (IGeoFeature : IAuditable).
        await _db.SaveChangesAsync();
        return GeometryUpdateResult.Ok(ToResponse(entity));
    }

    // Counts features that INTERSECT `area` (`.Intersects()` -> PostGIS ST_Intersects). Private
    // drawings remain scoped to the caller; POIs, stops, and routes are shared map catalogues, so all
    // visible rows participate. A route without built geometry cannot intersect and is skipped. Per
    // the analysis-tool spec even a slight overlap counts; full containment is NOT required. This
    // deliberately differs from saved-polygon counting above, which uses ST_Within. Counts run
    // sequentially because a single DbContext is not thread-safe (no Task.WhenAll).
    public async Task<AnalysisResponse?> AnalyzeAsync(string wkt, int userId)
    {
        Geometry area;
        try
        {
            area = _wktReader.Read(wkt);
        }
        catch
        {
            return null;
        }

        if (area is null || area.IsEmpty || !area.IsValid || area.OgcGeometryType != OgcGeometryType.Polygon)
            return null;

        area.SRID = Srid;

        var points = await _db.Points.CountAsync(x => x.UserId == userId && x.Geom.Intersects(area));
        var lines = await _db.Lines.CountAsync(x => x.UserId == userId && x.Geom.Intersects(area));
        var polygons = await _db.Polygons.CountAsync(x => x.UserId == userId && x.Geom.Intersects(area));
        var pois = await _db.Pois.CountAsync(x => x.Geom.Intersects(area));
        var stops = await _db.Stops.CountAsync(x => x.Geom.Intersects(area));
        var routes = await _db.Routes.CountAsync(x =>
            x.Geometry != null && x.Geometry.Intersects(area));

        return new AnalysisResponse
        {
            Points = points,
            Lines = lines,
            Polygons = polygons,
            Pois = pois,
            Stops = stops,
            Routes = routes,
            Total = points + lines + polygons + pois + stops + routes,
        };
    }

    // The query panel's contract: filter, sort, AND page in SQL — the client never trims rows itself.
    // The three tables share an identical projected shape, so Queryable.Concat chains them into a
    // single UNION ALL; CountAsync and OrderBy/Skip/Take then compose over that union in one statement
    // (EF Core translates set operations when both operands project to the same member-init shape).
    // The global !IsDeleted && IsActive filter applies inside each operand automatically. Returns null
    // for values outside the SortBy/SortDir/Types whitelists (the controller turns that into a 400).
    public async Task<GeometryQueryResponse?> QueryPageAsync(GeometryQueryRequest request, int userId)
    {
        var sortBy = request.SortBy.Trim().ToLowerInvariant();
        var sortDir = request.SortDir.Trim().ToLowerInvariant();
        if (sortBy is not ("name" or "type" or "createdat") || sortDir is not ("asc" or "desc"))
            return null;

        // CSV type filter -> the tables to union. Blank means all three; an unknown token is a 400,
        // not silently ignored, so a typo like "poligon" can't masquerade as an empty result.
        string[] included = string.IsNullOrWhiteSpace(request.Types)
            ? ["point", "line", "polygon"]
            : request.Types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant()).Distinct().ToArray();
        if (included.Length == 0 || included.Any(t => t is not ("point" or "line" or "polygon")))
            return null;

        // Case-insensitive "contains" via ILIKE. The user's term is escaped so %, _ and \ match
        // literally — otherwise searching "100%" would match every row.
        string? pattern = null;
        var term = request.Name?.Trim();
        if (!string.IsNullOrEmpty(term))
            pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        IQueryable<GeometryListItem>? query = null;
        foreach (var type in included)
        {
            var part = type switch
            {
                "point" => ProjectRows(_db.Points, "point", userId, pattern),
                "line" => ProjectRows(_db.Lines, "line", userId, pattern),
                _ => ProjectRows(_db.Polygons, "polygon", userId, pattern),
            };
            query = query is null ? part : query.Concat(part);
        }

        // Two sequential awaits on purpose: one DbContext is not thread-safe (no Task.WhenAll).
        var total = await query!.CountAsync();

        var ordered = (sortBy, descending: sortDir == "desc") switch
        {
            ("name", false) => query!.OrderBy(r => r.Name),
            ("name", true) => query!.OrderByDescending(r => r.Name),
            ("type", false) => query!.OrderBy(r => r.Type),
            ("type", true) => query!.OrderByDescending(r => r.Type),
            ("createdat", false) => query!.OrderBy(r => r.CreatedAt),
            _ => query!.OrderByDescending(r => r.CreatedAt),
        };

        // (Type, Id) tie-break makes the order total: Id alone repeats across the union (point 3 vs
        // line 3), and LIMIT/OFFSET over a non-total order can duplicate or drop rows between pages.
        var items = await ordered
            .ThenBy(r => r.Type)
            .ThenBy(r => r.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return new GeometryQueryResponse
        {
            Items = items,
            Total = total,
            Page = request.Page,
            PageSize = request.PageSize,
        };
    }

    // One table's contribution to the union: the caller's rows, optionally name-filtered, projected
    // to the shared row shape with its type discriminator baked in.
    private static IQueryable<GeometryListItem> ProjectRows<T>(
        DbSet<T> set, string type, int userId, string? namePattern)
        where T : class, IGeoFeature
    {
        IQueryable<T> q = set.Where(x => x.UserId == userId);
        if (namePattern is not null)
            q = q.Where(x => x.Name != null && EF.Functions.ILike(x.Name, namePattern, "\\"));
        return q.Select(x => new GeometryListItem
        {
            Id = x.Id,
            Type = type,
            Name = x.Name,
            Color = x.Color,
            CreatedAt = x.CreatedAt,
            ModifiedDate = x.ModifiedDate,
        });
    }

    private static GeometryResponse ToResponse(IGeoFeature entity) => new()
    {
        Id = entity.Id,
        // AsText() gives standard WKT (EPSG:4326) that OpenLayers parses directly.
        Wkt = entity.Geom.AsText(),
        Name = entity.Name,
        Color = entity.Color,
        CreatedAt = entity.CreatedAt,
        ModifiedDate = entity.ModifiedDate,
        ModifiedUserId = entity.ModifiedUserId,
    };
}
