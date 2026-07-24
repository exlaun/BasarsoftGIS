using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Union;

namespace Basarsoft.Api.Services;

public class GeoAuthorizationService : IGeoAuthorizationService
{
    private readonly AppDbContext _db;

    private readonly WKTReader _wktReader = new();

    private const int Srid = 4326;

    public GeoAuthorizationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<GeoAreaResponse?> GetForUserAsync(int userId)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId))
            return null;

        var row = await _db.GeoAuthorizations.FirstOrDefaultAsync(g => g.UserId == userId);
        return ToResponse(row);
    }

    public async Task<GeoAreaResponse?> GetForRoleAsync(int roleId)
    {
        if (!await _db.Roles.AnyAsync(r => r.Id == roleId))
            return null;

        var row = await _db.GeoAuthorizations.FirstOrDefaultAsync(g => g.RoleId == roleId);
        return ToResponse(row);
    }

    public async Task<GeoAreaWriteStatus> SetForUserAsync(int userId, string wkt)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId))
            return GeoAreaWriteStatus.NotFound;

        return await UpsertAsync(wkt, g => g.UserId == userId, geom => new GeoAuthorization
        {
            UserId = userId,
            Geom = geom,
        });
    }

    public async Task<GeoAreaWriteStatus> SetForRoleAsync(int roleId, string wkt)
    {
        if (!await _db.Roles.AnyAsync(r => r.Id == roleId))
            return GeoAreaWriteStatus.NotFound;

        return await UpsertAsync(wkt, g => g.RoleId == roleId, geom => new GeoAuthorization
        {
            RoleId = roleId,
            Geom = geom,
        });
    }

    public async Task<bool> ClearForUserAsync(int userId) =>
        await ClearAsync(g => g.UserId == userId);

    public async Task<bool> ClearForRoleAsync(int roleId) =>
        await ClearAsync(g => g.RoleId == roleId);

    // The boundary a user's drawings are checked against. Own area first (it OVERRIDES role areas —
    // the rule agreed with the mentor); otherwise the union of the user's roles' areas.
    public async Task<Geometry?> GetEffectiveAreaAsync(int userId)
    {
        var own = await _db.GeoAuthorizations
            .FirstOrDefaultAsync(g => g.UserId == userId && g.IsActive);
        if (own is not null)
            return own.Geom;

        // Joining through the filtered Roles set drops areas of soft-deleted roles automatically,
        // the same way effective permissions are resolved in UserAdminService.
        var roleAreas = await (from ur in _db.UserRoles
                               join r in _db.Roles on ur.RoleId equals r.Id
                               join g in _db.GeoAuthorizations on r.Id equals g.RoleId
                               where ur.UserId == userId && g.IsActive
                               select g.Geom).ToListAsync();

        if (roleAreas.Count == 0)
            return null;

        var area = roleAreas.Count == 1 ? roleAreas[0] : UnaryUnionOp.Union(roleAreas);
        area.SRID = Srid;
        return area;
    }

    // ---- helpers ----

    // A redraw replaces the live row's multipolygon (and revives a deactivated one); the first assignment
    // inserts. The partial unique index on user_id/role_id guarantees at most one live row per target.
    private async Task<GeoAreaWriteStatus> UpsertAsync(
        string wkt,
        System.Linq.Expressions.Expression<Func<GeoAuthorization, bool>> match,
        Func<Geometry, GeoAuthorization> create)
    {
        var geom = ParseArea(wkt);
        if (geom is null)
            return GeoAreaWriteStatus.InvalidGeometry;

        var row = await _db.GeoAuthorizations.FirstOrDefaultAsync(match);
        if (row is null)
        {
            _db.GeoAuthorizations.Add(create(geom));
        }
        else
        {
            row.Geom = geom;
            row.IsActive = true;
        }

        await _db.SaveChangesAsync();
        return GeoAreaWriteStatus.Success;
    }

    private async Task<bool> ClearAsync(System.Linq.Expressions.Expression<Func<GeoAuthorization, bool>> match)
    {
        var row = await _db.GeoAuthorizations.FirstOrDefaultAsync(match);
        if (row is null)
            return false;

        row.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    // Same guard set as geometry saves: parseable WKT, non-empty, Polygon/MultiPolygon, and
    // topologically valid (a self-intersecting boundary would make Covers checks meaningless).
    // Plain polygons are wrapped so the PostGIS typmod always receives a MultiPolygon.
    private Geometry? ParseArea(string wkt)
    {
        Geometry geom;
        try
        {
            geom = _wktReader.Read(wkt);
        }
        catch
        {
            return null;
        }

        if (geom is null || geom.IsEmpty)
            return null;

        MultiPolygon normalized;
        if (geom is Polygon polygon)
        {
            if (!polygon.IsValid)
                return null;
            normalized = polygon.Factory.CreateMultiPolygon([polygon]);
        }
        else if (geom is MultiPolygon multiPolygon)
        {
            // OpenLayers edits disconnected components independently. If a user draws components
            // that touch or overlap, their raw MultiPolygon is OGC-invalid even though every
            // component is a valid polygon. Dissolve those components into their polygonal union
            // instead of making the form fail with a mysterious 400. A self-intersecting component
            // remains invalid and is still rejected.
            var components = multiPolygon.Geometries.Cast<Polygon>().ToArray();
            if (components.Length == 0 || components.Any(component => !component.IsValid))
                return null;
            Geometry dissolved = components[0].Copy();
            foreach (var component in components.Skip(1))
                dissolved = dissolved.Union(component);
            if (dissolved.IsEmpty || !dissolved.IsValid)
                return null;
            normalized = dissolved switch
            {
                Polygon dissolvedPolygon =>
                    dissolvedPolygon.Factory.CreateMultiPolygon([dissolvedPolygon]),
                MultiPolygon dissolvedMultiPolygon => dissolvedMultiPolygon,
                _ => null!,
            };
            if (normalized is null)
                return null;
        }
        else
        {
            return null;
        }

        normalized.SRID = Srid;
        return normalized;
    }

    private static GeoAreaResponse ToResponse(GeoAuthorization? row) => new()
    {
        Wkt = row?.IsActive == true ? row.Geom.AsText() : null,
        ModifiedDate = row?.IsActive == true ? row.ModifiedDate : null,
    };
}
