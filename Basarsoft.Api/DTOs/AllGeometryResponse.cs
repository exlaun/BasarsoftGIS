namespace Basarsoft.Api.DTOs;

// Returned by GET /api/geometry so the map can load every shape the user owns in one request.
public class AllGeometryResponse
{
    public IReadOnlyList<GeometryResponse> Points { get; set; } = new List<GeometryResponse>();
    public IReadOnlyList<GeometryResponse> Lines { get; set; } = new List<GeometryResponse>();
    public IReadOnlyList<GeometryResponse> Polygons { get; set; } = new List<GeometryResponse>();
}
