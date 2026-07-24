using System.Text.Json.Serialization;

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

    // Last-edited timestamp, stamped automatically on every insert/update. Surfaced so the info popup
    // can show when a shape was last changed (also the visible evidence that edit-tracking works).
    public DateTime ModifiedDate { get; set; }

    // Who last changed the shape (users.id), the companion of ModifiedDate. Null only for rows that
    // predate the column and were never touched since.
    public int? ModifiedUserId { get; set; }

    // Only populated when a polygon is created: how many of the caller's existing inventories touch
    // or cross it. Hidden on point/line/read/update responses.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? IntersectionCount { get; set; }
}
