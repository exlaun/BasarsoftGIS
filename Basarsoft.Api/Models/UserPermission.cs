namespace Basarsoft.Api.Models;

// Join row granting a permission DIRECTLY to a user, independent of any role (many-to-many). Has its
// own sequence-backed Id; the pair (UserId, PermissionId) stays unique via an index, configured in
// AppDbContext.OnModelCreating.
public class UserPermission : IAuditable
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int PermissionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
