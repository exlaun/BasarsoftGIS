using Basarsoft.Api.DTOs;
using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Services;

// Outcome of assigning an area: saved, the target user/role doesn't exist, or the WKT wasn't a
// single valid polygon. Mirrors the status-enum pattern used by GeometryUpdateResult.
public enum GeoAreaWriteStatus
{
    Success,
    NotFound,
    InvalidGeometry,
}

public interface IGeoAuthorizationService
{
    // The target's assigned area (Wkt null = none assigned). Returns null if the user/role is missing.
    Task<GeoAreaResponse?> GetForUserAsync(int userId);
    Task<GeoAreaResponse?> GetForRoleAsync(int roleId);

    // Upserts the target's area from polygon WKT (a redraw replaces the previous polygon).
    Task<GeoAreaWriteStatus> SetForUserAsync(int userId, string wkt);
    Task<GeoAreaWriteStatus> SetForRoleAsync(int roleId, string wkt);

    // Soft-deletes the target's area. False when the target is missing or had no area.
    Task<bool> ClearForUserAsync(int userId);
    Task<bool> ClearForRoleAsync(int roleId);

    // The polygon a user's drawings must stay inside, or null for unrestricted. The user's own area
    // overrides role areas; otherwise the union of the user's roles' areas (may be a MultiPolygon).
    Task<Geometry?> GetEffectiveAreaAsync(int userId);

    // True when the caller is area-bound and `geom` is not fully inside that area. Covers (not
    // Within) so a feature touching the boundary stays legal, and a null area (nobody assigned one)
    // means unrestricted. Defined here as a default implementation so every implementation — the
    // real service and the test fakes alike — derives it from GetEffectiveAreaAsync and cannot
    // drift. When testing several geometries against one area, call GetEffectiveAreaAsync once and
    // compare in a loop instead of calling this per geometry.
    async Task<bool> IsOutsideAreaAsync(int userId, Geometry geom)
    {
        var area = await GetEffectiveAreaAsync(userId);
        return area is not null && !area.Covers(geom);
    }
}
