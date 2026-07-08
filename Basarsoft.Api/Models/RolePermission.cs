namespace Basarsoft.Api.Models;

// Join row granting a permission to a role (many-to-many). Has its own sequence-backed Id; the pair
// (RoleId, PermissionId) stays unique via an index, configured in AppDbContext.OnModelCreating.
public class RolePermission : IAuditable
{
    public int Id { get; set; }

    public int RoleId { get; set; }

    public int PermissionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
