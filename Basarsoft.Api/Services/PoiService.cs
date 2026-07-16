using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace Basarsoft.Api.Services;

public class PoiService : IPoiService
{
    private readonly AppDbContext _db;
    private readonly IGeoAuthorizationService _geoAuth;
    private readonly WKTReader _wktReader = new();

    // Same storage CRS as every other geometry table: EPSG:4326 (WGS84 lon-lat).
    private const int Srid = 4326;

    public PoiService(AppDbContext db, IGeoAuthorizationService geoAuth)
    {
        _db = db;
        _geoAuth = geoAuth;
    }

    public async Task<PoiWriteResult> CreateAsync(PoiCreateRequest request, int userId)
    {
        // Parse the WKT the client drew. Bad text -> InvalidGeometry -> the controller returns 400.
        Geometry geom;
        try
        {
            geom = _wktReader.Read(request.Wkt);
        }
        catch
        {
            return PoiWriteResult.InvalidGeometry;
        }

        if (geom is null || geom.IsEmpty || geom.OgcGeometryType != OgcGeometryType.Point)
            return PoiWriteResult.InvalidGeometry;

        geom.SRID = Srid;

        // The dropdown is admin-curated, but a direct API call could still send a stale/garbage id.
        var categoryId = request.CategoryId!.Value;
        if (!await _db.PoiCategories.AnyAsync(c => c.Id == categoryId))
            return PoiWriteResult.CategoryNotFound;

        // Operators are bound by the same geographic authorization as the drawing tools: with an
        // assigned area, POIs may only be placed inside it (Covers keeps boundary points legal).
        var area = await _geoAuth.GetEffectiveAreaAsync(userId);
        if (area is not null && !area.Covers(geom))
            return PoiWriteResult.OutsideAuthorizedArea;

        var poi = new Poi
        {
            UserId = userId,
            Name = request.Name,
            Geom = geom,
            CategoryId = categoryId,
            OpenTime = request.OpenTime!.Value,
            CloseTime = request.CloseTime!.Value,
            CreatedAt = DateTime.UtcNow,
            // A never-edited POI reports its creator as the last modifier, matching the geometry tables.
            ModifiedUserId = userId,
        };
        _db.Pois.Add(poi);
        await _db.SaveChangesAsync();

        var categories = await CategoryMapAsync();
        var username = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync() ?? string.Empty;

        return PoiWriteResult.Ok(ToResponse(poi, categories, new Dictionary<int, string> { [userId] = username }));
    }

    public async Task<bool> DeleteAsync(int id, int userId, bool isAdmin)
    {
        // Ownership is part of the WHERE clause for non-admins, so "not yours" and "doesn't exist"
        // are indistinguishable to the caller (both 404) — no information leak about others' rows.
        var poi = await _db.Pois.FirstOrDefaultAsync(p => p.Id == id && (isAdmin || p.UserId == userId));
        if (poi is null)
            return false;

        poi.IsDeleted = true;
        poi.ModifiedUserId = userId;
        await _db.SaveChangesAsync();
        return true;
    }

    private async Task<Dictionary<int, PoiCategory>> CategoryMapAsync() =>
        await _db.PoiCategories.ToDictionaryAsync(c => c.Id);

    private static PoiResponse ToResponse(
        Poi poi, IReadOnlyDictionary<int, PoiCategory> categories, IReadOnlyDictionary<int, string> usernames)
    {
        categories.TryGetValue(poi.CategoryId, out var category);
        return new PoiResponse
        {
            Id = poi.Id,
            Wkt = poi.Geom.AsText(),
            Name = poi.Name ?? string.Empty,
            CategoryId = poi.CategoryId,
            CategoryName = category?.Name ?? string.Empty,
            CategoryPath = BuildPath(poi.CategoryId, categories),
            CategoryColor = EffectiveColor(poi.CategoryId, categories),
            OpenTime = poi.OpenTime,
            CloseTime = poi.CloseTime,
            UserId = poi.UserId,
            CreatedBy = usernames.TryGetValue(poi.UserId, out var name) ? name : string.Empty,
            CreatedAt = poi.CreatedAt,
            ModifiedDate = poi.ModifiedDate,
        };
    }

    // Root-first breadcrumb ("Yeme İçme > Restoran") built by walking ParentId links in memory.
    // The depth cap makes a corrupt parent cycle degrade to a truncated path instead of hanging.
    private static string BuildPath(int categoryId, IReadOnlyDictionary<int, PoiCategory> categories)
    {
        var parts = new List<string>();
        var currentId = (int?)categoryId;
        for (var depth = 0; currentId is not null && depth < 20; depth++)
        {
            if (!categories.TryGetValue(currentId.Value, out var category))
                break;
            parts.Add(category.Name);
            currentId = category.ParentId;
        }

        parts.Reverse();
        return string.Join(" > ", parts);
    }

    // Effective marker color: the category's own color, else the nearest ancestor's. Same walk and
    // cycle cap as BuildPath; null all the way up means "use the client's default POI color".
    private static string? EffectiveColor(int categoryId, IReadOnlyDictionary<int, PoiCategory> categories)
    {
        var currentId = (int?)categoryId;
        for (var depth = 0; currentId is not null && depth < 20; depth++)
        {
            if (!categories.TryGetValue(currentId.Value, out var category))
                break;
            if (category.Color is not null)
                return category.Color;
            currentId = category.ParentId;
        }

        return null;
    }
}
