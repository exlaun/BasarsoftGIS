namespace Basarsoft.Api.Models;

// Join row linking a user to a role (many-to-many). Has its own sequence-backed Id; the pair
// (UserId, RoleId) stays unique via an index, configured in AppDbContext.OnModelCreating.
public class UserRole : IAuditable
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int RoleId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
