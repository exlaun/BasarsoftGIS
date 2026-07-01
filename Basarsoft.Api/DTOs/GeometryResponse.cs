namespace Basarsoft.Api.DTOs;

// Returned by the geometry read endpoints. The geometry is handed back as WKT text (EPSG:4326),
// which the OpenLayers client parses straight back into a feature.
public class GeometryResponse
{
    public int Id { get; set; }

    public string Wkt { get; set; } = string.Empty;

    public string? Name { get; set; }

    // Display color (hex) chosen by the user; null for older rows saved before this field existed.
    public string? Color { get; set; }

    public DateTime CreatedAt { get; set; }

    // Only populated when a polygon is created: how many of the caller's existing shapes fall inside it.
    // Null for point/line saves and for read endpoints.
    public int? IntersectionCount { get; set; }
}
