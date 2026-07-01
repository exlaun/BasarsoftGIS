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

        return type.ToLowerInvariant() switch
        {
            "point" when geom.OgcGeometryType == OgcGeometryType.Point
                => await AddAsync(_db.Points, geom, request.Name, userId),
            "line" when geom.OgcGeometryType == OgcGeometryType.LineString
                => await AddAsync(_db.Lines, geom, request.Name, userId),
            "polygon" when geom.OgcGeometryType == OgcGeometryType.Polygon
                => await AddAsync(_db.Polygons, geom, request.Name, userId),
            _ => null, // unknown type or WKT geometry doesn't match the table
        };
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

    private async Task<GeometryResponse?> AddAsync<T>(DbSet<T> set, Geometry geom, string? name, int userId)
        where T : class, IGeoFeature, new()
    {
        var entity = new T
        {
            UserId = userId,
            Name = name,
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

    private static GeometryResponse ToResponse(IGeoFeature entity) => new()
    {
        Id = entity.Id,
        // AsText() gives standard WKT (EPSG:4326) that OpenLayers parses directly.
        Wkt = entity.Geom.AsText(),
        Name = entity.Name,
        CreatedAt = entity.CreatedAt,
    };
}
