using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Models;

// One drawn line -> tbl_line. Geom column is constrained to geometry(LineString,4326) in AppDbContext.
public class LineFeature : IGeoFeature
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
