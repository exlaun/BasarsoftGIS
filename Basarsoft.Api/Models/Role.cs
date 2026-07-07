namespace Basarsoft.Api.Models;

// A named role that bundles a set of permissions. Users are linked to roles via user_roles, and a
// role's permissions flow to every user who holds it (see UserAdminService effective-permission logic).
public class Role : IAuditable
{
    public int Id { get; set; }

    // Human-readable role name, e.g. "Admin". Unique (enforced by an index in AppDbContext).
    public string Name { get; set; } = string.Empty;

    // Optional free-text description of what the role is for.
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Soft delete: hidden everywhere via a global query filter instead of being physically removed.
    public bool IsDeleted { get; set; }

    // A disabled role still exists but is treated as inactive.
    public bool IsActive { get; set; } = true;

    // "Last edited" timestamp, stamped automatically in AppDbContext.SaveChanges.
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
