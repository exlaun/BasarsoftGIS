using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Models;

// One drawn point -> tbl_point. Geom column is constrained to geometry(Point,4326) in AppDbContext.
public class PointFeature : IGeoFeature
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? Name { get; set; }
    public Geometry Geom { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
