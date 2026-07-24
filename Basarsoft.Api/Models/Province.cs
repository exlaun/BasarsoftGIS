using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Models;

// One Turkish province (il) -> tbl_province. Static reference data for the location-analysis tool's
// "pick a province" region option, synchronized from Data/provinces.geojson (exact OSM
// network=TR-provinces admin_level=4 relations). Unlike the drawing tables there is no owning user:
// the rows are system data, so only the minimal audit pair (CreatedAt + IAuditable's ModifiedDate)
// applies.
public class Province : IAuditable
{
    public int Id { get; set; }

    // Province display name, e.g. "Ankara". Unique — the dropdown is keyed by it visually.
    public string Name { get; set; } = string.Empty;

    // The province boundary. MultiPolygon because some provinces include islands (WGS84 lon-lat).
    public Geometry Geom { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
