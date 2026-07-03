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

    // Turns WKT text into a NetTopologySuite geometry. Reusable within this scoped (per-request) service.
    private readonly WKTReader _wktReader = new();

    // The coordinate system we store everything in: EPSG:4326 (WGS84 lon-lat).
    private const int Srid = 4326;

    public GeometryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<GeometryResponse?> CreateAsync(string type, GeometryCreateRequest request, int userId)
    {
        // Parse the WKT the client drew. Bad text -> null -> the controller returns 400.
        Geometry geom;
        try
        {
            geom = _wktReader.Read(request.Wkt);
        }
        catch
        {
            return null;
        }

        if (geom is null || geom.IsEmpty)
            return null;

        // The DB columns are typed (geometry(Point,4326) etc.), so the WKT must match the endpoint.
        geom.SRID = Srid;

        switch (type.ToLowerInvariant())
        {
            case "point" when geom.OgcGeometryType == OgcGeometryType.Point:
                return await AddAsync(_db.Points, geom, request, userId);
            case "line" when geom.OgcGeometryType == OgcGeometryType.LineString:
                return await AddAsync(_db.Lines, geom, request, userId);
            case "polygon" when geom.OgcGeometryType == OgcGeometryType.Polygon:
                return await AddAsync(_db.Polygons, geom, request, userId);
            default:
                return null; // unknown type or WKT geometry doesn't match the table
        }
    }

    public Task<IReadOnlyList<GeometryResponse>> ListAsync(string type, int userId) =>
        type.ToLowerInvariant() switch
        {
            "point" => QueryAsync(_db.Points, userId),
            "line" => QueryAsync(_db.Lines, userId),
            "polygon" => QueryAsync(_db.Polygons, userId),
            _ => Task.FromResult<IReadOnlyList<GeometryResponse>>(Array.Empty<GeometryResponse>()),
        };

    public async Task<AllGeometryResponse> ListAllAsync(int userId) => new()
    {
        Points = await QueryAsync(_db.Points, userId),
        Lines = await QueryAsync(_db.Lines, userId),
        Polygons = await QueryAsync(_db.Polygons, userId),
    };

    public Task<GeometryUpdateResult> UpdateAsync(string type, int id, GeometryUpdateRequest request, int userId) =>
        type.ToLowerInvariant() switch
        {
            "point" => UpdateEntityAsync(_db.Points, id, request, userId, OgcGeometryType.Point),
            "line" => UpdateEntityAsync(_db.Lines, id, request, userId, OgcGeometryType.LineString),
            "polygon" => UpdateEntityAsync(_db.Polygons, id, request, userId, OgcGeometryType.Polygon),
            _ => Task.FromResult(GeometryUpdateResult.NotFound),
        };

    public Task<bool> DeleteAsync(string type, int id, int userId) =>
        type.ToLowerInvariant() switch
        {
            "point" => SoftDeleteAsync(_db.Points, id, userId),
            "line" => SoftDeleteAsync(_db.Lines, id, userId),
            "polygon" => SoftDeleteAsync(_db.Polygons, id, userId),
            _ => Task.FromResult(false),
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

    private async Task<bool> SoftDeleteAsync<T>(DbSet<T> set, int id, int userId)
        where T : class, IGeoFeature
    {
        // Ownership check baked in: you can only delete your own shape.
        var entity = await set.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (entity is null)
            return false;

        entity.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
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

            if (geom is null || geom.IsEmpty || geom.OgcGeometryType != expectedType)
                return GeometryUpdateResult.InvalidGeometry;

            geom.SRID = Srid;
            entity.Geom = geom;
        }

        entity.Name = request.Name;
        entity.Color = request.Color;
        // ModifiedDate is stamped automatically by AppDbContext.SaveChanges (IGeoFeature : IAuditable).
        await _db.SaveChangesAsync();
        return GeometryUpdateResult.Ok(ToResponse(entity));
    }

    // Counts the caller's shapes that INTERSECT `area` (`.Intersects()` -> PostGIS ST_Intersects). Per
    // the analysis-tool spec a shape counts even if it only slightly overlaps the polygon — full
    // containment is NOT required (this is the deliberate difference from the earlier draw-time count,
    // which used ST_Within). The three counts run sequentially because a single DbContext is not
    // thread-safe (no Task.WhenAll). Scope is the caller's own shapes, matching the per-user isolation
    // used everywhere else — widen the UserId filter here for a shared, cross-user inventory.
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

        if (area is null || area.IsEmpty || area.OgcGeometryType != OgcGeometryType.Polygon)
            return null;

        area.SRID = Srid;

        var points = await _db.Points.CountAsync(x => x.UserId == userId && x.Geom.Intersects(area));
        var lines = await _db.Lines.CountAsync(x => x.UserId == userId && x.Geom.Intersects(area));
        var polygons = await _db.Polygons.CountAsync(x => x.UserId == userId && x.Geom.Intersects(area));

        return new AnalysisResponse
        {
            Points = points,
            Lines = lines,
            Polygons = polygons,
            Total = points + lines + polygons,
        };
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
    };
}
