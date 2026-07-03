using Basarsoft.Api.DTOs;

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

    // Updates the caller's own shape: its name/color always, and its geometry when the request
    // carries WKT. Returns Success (with the updated row), NotFound (missing or not theirs), or
    // InvalidGeometry (bad WKT / wrong geometry type for this shape).
    Task<GeometryUpdateResult> UpdateAsync(string type, int id, GeometryUpdateRequest request, int userId);

    // Soft-deletes the caller's own shape. Returns false if it doesn't exist or isn't theirs.
    Task<bool> DeleteAsync(string type, int id, int userId);

    // Counts how many of the caller's shapes (points + lines + polygons) intersect the polygon given
    // as WKT — even a small overlap counts (ST_Intersects). The polygon is NOT saved. Returns null if
    // the WKT is invalid or isn't a polygon.
    Task<AnalysisResponse?> AnalyzeAsync(string wkt, int userId);
}
