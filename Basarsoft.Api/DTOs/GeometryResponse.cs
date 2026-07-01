namespace Basarsoft.Api.DTOs;

// Returned by the geometry read endpoints. The geometry is handed back as WKT text (EPSG:4326),
// which the OpenLayers client parses straight back into a feature.
public class GeometryResponse
{
    public int Id { get; set; }

    public string Wkt { get; set; } = string.Empty;

    public string? Name { get; set; }

    public DateTime CreatedAt { get; set; }
}
