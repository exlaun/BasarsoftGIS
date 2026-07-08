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
    // the rule agreed with the mentor); otherwise the union of the user's roles' areas, which may
    // come out of the union operation as a MultiPolygon (in-memory only, never stored).
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

    // A redraw replaces the live row's polygon (and revives a deactivated one); the first assignment
    // inserts. The partial unique index on user_id/role_id guarantees at most one live row per target.
    private async Task<GeoAreaWriteStatus> UpsertAsync(
        string wkt,
        System.Linq.Expressions.Expression<Func<GeoAuthorization, bool>> match,
        Func<Geometry, GeoAuthorization> create)
    {
        var geom = ParsePolygon(wkt);
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

    // Same guard set as geometry saves: parseable WKT, non-empty, exactly a POLYGON, and topologically
    // valid (a self-intersecting boundary would make Covers checks meaningless).
    private Geometry? ParsePolygon(string wkt)
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

        if (geom is null || geom.IsEmpty || geom.OgcGeometryType != OgcGeometryType.Polygon || !geom.IsValid)
            return null;

        geom.SRID = Srid;
        return geom;
    }

    private static GeoAreaResponse ToResponse(GeoAuthorization? row) => new()
    {
        Wkt = row?.IsActive == true ? row.Geom.AsText() : null,
        ModifiedDate = row?.IsActive == true ? row.ModifiedDate : null,
    };
}
