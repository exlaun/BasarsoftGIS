using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Models;

// One point of interest -> tbl_poi. Implements IGeoFeature so it reuses the shared geometry-table
// mapping (sequence id, geometry(Point,4326) column, soft-delete filter, user FKs). Unlike the
// drawing tables, POIs are a shared catalogue: every user sees all of them; only adding is gated.
public class Poi : IGeoFeature
{
    public int Id { get; set; }

    // FK -> users.id. The operator who added the POI ("ekleyen") — shown in the admin list.
    public int UserId { get; set; }

    public string? Name { get; set; }

    // Unused for POIs (they render with one fixed marker style), but required by IGeoFeature.
    public string? Color { get; set; }

    public Geometry Geom { get; set; } = default!;

    // FK -> tbl_poi_category.id. Every POI belongs to exactly one category from the admin's tree.
    public int CategoryId { get; set; }

    // Daily working hours (mesai saatleri), e.g. 09:00 - 18:00. TimeOnly maps to Postgres `time`.
    public TimeOnly OpenTime { get; set; }

    public TimeOnly CloseTime { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    public bool IsActive { get; set; } = true;

    public int? ModifiedUserId { get; set; }

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
