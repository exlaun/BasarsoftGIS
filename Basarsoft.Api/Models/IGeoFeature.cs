using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Models;

// The shared shape of every drawn-geometry row (tbl_point / tbl_line / tbl_polygon).
// They differ only in which geometry type the DB column is constrained to; in C# we hold the
// base Geometry type so one generic service can handle all three.
//
// Extends IAuditable so AppDbContext.SaveChanges stamps ModifiedDate automatically on every
// insert/update — the same tracking the User entity already gets.
public interface IGeoFeature : IAuditable
{
    int Id { get; set; }

    // FK -> users.id. Records who drew the shape. Stamped from the JWT on save, never from the client.
    int UserId { get; set; }

    // Optional human label for the shape.
    string? Name { get; set; }

    // Optional display color for the shape (hex string, e.g. "#2563eb"). Chosen by the user on save.
    string? Color { get; set; }

    // The shape itself, stored as a PostGIS geometry (SRID 4326 / WGS84 lon-lat).
    Geometry Geom { get; set; }

    DateTime CreatedAt { get; set; }

    // Soft delete: hidden via a global query filter instead of being physically removed.
    bool IsDeleted { get; set; }

    // Whether the shape is enabled. An inactive (is_active = false) shape still exists in the DB but
    // is hidden from the map by the same global query filter. Kept for parity with the users table's
    // audit columns (the mentor asked every drawing table to carry the full audit set).
    bool IsActive { get; set; }

    // FK -> users.id. WHO last changed the shape (create / update / soft delete), the companion of
    // ModifiedDate's WHEN. Stamped from the JWT in the service layer — SaveChanges can't do it because
    // the DbContext has no user context. Nullable: rows from before this column can't know their editor.
    int? ModifiedUserId { get; set; }

    // ModifiedDate is inherited from IAuditable (stamped in AppDbContext.SaveChanges).
}
