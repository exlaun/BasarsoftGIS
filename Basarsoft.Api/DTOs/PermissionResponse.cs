namespace Basarsoft.Api.DTOs;

// One permission as shown in the admin permission list and referenced by the assignment editors.
public class PermissionResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
