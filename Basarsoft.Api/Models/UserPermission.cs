namespace Basarsoft.Api.Models;

// Join row granting a permission DIRECTLY to a user, independent of any role (many-to-many). The pair
// (UserId, PermissionId) is the composite key, configured in AppDbContext.OnModelCreating.
public class UserPermission
{
    public int UserId { get; set; }

    public int PermissionId { get; set; }
}
