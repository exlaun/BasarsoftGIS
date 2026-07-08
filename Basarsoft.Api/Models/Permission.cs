namespace Basarsoft.Api.Models;

// A single capability that can be granted to a role or directly to a user, e.g. "add_point".
// The seeded set is the shared permission list the admin UI assigns from.
public class Permission : IAuditable
{
    public int Id { get; set; }

    // Machine key for the permission, e.g. "add_point". Unique (enforced by an index in AppDbContext).
    public string Name { get; set; } = string.Empty;

    // Human-readable explanation of what the permission allows.
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Soft delete: hidden everywhere via a global query filter instead of being physically removed.
    public bool IsDeleted { get; set; }

    // A disabled permission still exists but is treated as inactive.
    public bool IsActive { get; set; } = true;

    // "Last edited" timestamp, stamped automatically in AppDbContext.SaveChanges.
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
