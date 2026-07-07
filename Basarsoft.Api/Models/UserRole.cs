namespace Basarsoft.Api.Models;

// Join row linking a user to a role (many-to-many). The pair (UserId, RoleId) is the composite key,
// configured in AppDbContext.OnModelCreating.
public class UserRole
{
    public int UserId { get; set; }

    public int RoleId { get; set; }
}
