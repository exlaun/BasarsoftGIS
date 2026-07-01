using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Models;

// The shared shape of every drawn-geometry row (tbl_point / tbl_line / tbl_polygon).
// They differ only in which geometry type the DB column is constrained to; in C# we hold the
// base Geometry type so one generic service can handle all three.
public interface IGeoFeature
{
    int Id { get; set; }

    // FK -> users.id. Records who drew the shape. Stamped from the JWT on save, never from the client.
    int UserId { get; set; }

    // Optional human label for the shape.
    string? Name { get; set; }

    // The shape itself, stored as a PostGIS geometry (SRID 4326 / WGS84 lon-lat).
    Geometry Geom { get; set; }

    DateTime CreatedAt { get; set; }

    // Soft delete: hidden via a global query filter instead of being physically removed.
    bool IsDeleted { get; set; }
}
