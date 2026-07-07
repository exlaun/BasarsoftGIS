namespace Basarsoft.Api.Models;

// Join row granting a permission to a role (many-to-many). The pair (RoleId, PermissionId) is the
// composite key, configured in AppDbContext.OnModelCreating.
public class RolePermission
{
    public int RoleId { get; set; }

    public int PermissionId { get; set; }
}
