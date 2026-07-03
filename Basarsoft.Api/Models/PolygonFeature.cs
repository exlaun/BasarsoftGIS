using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Models;

// One drawn polygon -> tbl_polygon. Geom column is constrained to geometry(Polygon,4326) in AppDbContext.
public class PolygonFeature : IGeoFeature
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? Name { get; set; }
    public string? Color { get; set; }
    public Geometry Geom { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
