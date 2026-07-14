namespace Basarsoft.Api.Models;

// One POI category -> tbl_poi_category. Categories form a tree: ParentId points at another row in
// this same table (null = top-level), e.g. "Yeme İçme" (parent) -> "Restoran", "Cafe" (children).
public class PoiCategory : IAuditable
{
    public int Id { get; set; }

    // Category label shown in the admin tree and the operator's dropdown, e.g. "Restoran".
    public string Name { get; set; } = string.Empty;

    // Self-referencing FK -> tbl_poi_category.id. Null for a root category. Restrict-on-delete plus a
    // service-level guard keep a category from disappearing while children still point at it.
    public int? ParentId { get; set; }

    // FK -> users.id. The admin who created the category. Stamped from the JWT, never from the client.
    public int UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Soft delete: hidden via a global query filter instead of being physically removed.
    public bool IsDeleted { get; set; }

    public bool IsActive { get; set; } = true;

    // FK -> users.id. Who last changed the category (rename / re-parent / soft delete).
    public int? ModifiedUserId { get; set; }

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
