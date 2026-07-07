namespace Basarsoft.Api.DTOs;

// One user row for the admin Users screen, including the roles the user currently holds.
public class AdminUserResponse
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ModifiedDate { get; set; }

    public IReadOnlyList<RoleSummary> Roles { get; set; } = Array.Empty<RoleSummary>();
}
