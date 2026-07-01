using Basarsoft.Api.DTOs;
using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Services;

public interface IGeometryService
{
    // Saves a shape (from WKT) into the table for `type` (point|line|polygon), owned by `userId`.
    // Returns the saved row as WKT, or null if the type is unknown / the WKT is invalid or the
    // wrong geometry type for that table.
    Task<GeometryResponse?> CreateAsync(string type, GeometryCreateRequest request, int userId);

    // Lists the caller's non-deleted shapes of one type as WKT. Empty if the type is unknown.
    Task<IReadOnlyList<GeometryResponse>> ListAsync(string type, int userId);

    // Lists all of the caller's shapes (points + lines + polygons) for a one-shot map load.
    Task<AllGeometryResponse> ListAllAsync(int userId);

    // Soft-deletes the caller's own shape. Returns false if it doesn't exist or isn't theirs.
    Task<bool> DeleteAsync(string type, int id, int userId);

    // Counts how many of the caller's existing shapes (points + lines + polygons) fall strictly inside
    // `area` (ST_Within). Used after a polygon is drawn to report how many inventories are inside it.
    Task<int> CountContainedAsync(Geometry area, int userId);
}
