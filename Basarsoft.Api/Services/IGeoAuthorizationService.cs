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
}
