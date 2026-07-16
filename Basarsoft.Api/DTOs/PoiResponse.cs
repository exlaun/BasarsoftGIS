namespace Basarsoft.Api.DTOs;

// One POI as the map and the admin list consume it. Unlike GeometryResponse this carries the
// category (id + name + full "Yeme İçme > Restoran" path) and the creator's username, because the
// POI catalogue is shared — viewers need to see whose POI it is and where it sits in the tree.
public class PoiResponse
{
    public int Id { get; set; }

    // The point as WKT (EPSG:4326), parsed straight back into an OpenLayers feature.
    public string Wkt { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    // Root-first breadcrumb of the category tree, e.g. "Yeme İçme > Restoran".
    public string CategoryPath { get; set; } = string.Empty;

    // Effective marker color: the category's own color or the nearest ancestor's ("#rrggbb").
    // Null when the whole chain is colorless — the client then uses its default POI rose.
    public string? CategoryColor { get; set; }

    // Working hours; serialized by System.Text.Json as "HH:mm:ss" (the client trims to HH:mm).
    public TimeOnly OpenTime { get; set; }

    public TimeOnly CloseTime { get; set; }

    // users.id of the creator — lets the client decide whether to offer Delete (own POI or admin).
    public int UserId { get; set; }

    // The creator's username ("ekleyen") shown in the admin list and the info panel.
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ModifiedDate { get; set; }
}
