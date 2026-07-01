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
                // Count the caller's existing shapes that fall inside this area BEFORE inserting the new
                // polygon, otherwise the polygon would contain itself and inflate the count by one.
                var intersectionCount = await CountContainedAsync(geom, userId);
                var saved = await AddAsync(_db.Polygons, geom, request, userId);
                if (saved is not null)
                    saved.IntersectionCount = intersectionCount;
                return saved;
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

    // Counts the caller's existing (non-deleted) shapes that fall strictly INSIDE `area`. The mentor's
    // spec asks for "polygon içerisinde kalan" (items remaining inside the polygon), so we use containment
    // (`.Within()` -> PostGIS ST_Within) rather than a mere touch/overlap test: a shape that only clips
    // the polygon's edge is NOT counted. ST_Within runs in the database, and the global !IsDeleted query
    // filter applies automatically to each DbSet. Scope is the caller's own shapes (per the per-user
    // isolation design) — widen the UserId filter here if a shared, cross-user inventory is intended.
    public async Task<int> CountContainedAsync(Geometry area, int userId)
    {
        var points = await _db.Points.Where(x => x.UserId == userId && x.Geom.Within(area)).CountAsync();
        var lines = await _db.Lines.Where(x => x.UserId == userId && x.Geom.Within(area)).CountAsync();
        var polygons = await _db.Polygons.Where(x => x.UserId == userId && x.Geom.Within(area)).CountAsync();
        return points + lines + polygons;
    }

    private static GeometryResponse ToResponse(IGeoFeature entity) => new()
    {
        Id = entity.Id,
        // AsText() gives standard WKT (EPSG:4326) that OpenLayers parses directly.
        Wkt = entity.Geom.AsText(),
        Name = entity.Name,
        Color = entity.Color,
        CreatedAt = entity.CreatedAt,
    };
}
