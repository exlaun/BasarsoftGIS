namespace Basarsoft.Api.Models;

// Represents an application user stored in the database.
public class User : IAuditable
{
    // Primary key. EF Core automatically treats a property named "Id" as the PK.
    public int Id { get; set; }

    // The user's login name.
    public string Username { get; set; } = string.Empty;

    // The hashed password. We never store the plain-text password (hashing comes later with JWT).
    public string PasswordHash { get; set; } = string.Empty;

    // When the user record was created. We use UtcNow because Npgsql stores this as a UTC timestamp.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Soft delete: instead of physically removing the row, we flip this flag and hide the user
    // everywhere (via a global query filter). Reversible, and keeps history/references intact.
    public bool IsDeleted { get; set; }

    // Whether the account is enabled. A disabled (is_active = false) user still exists but can't log in.
    public bool IsActive { get; set; } = true;

    // "Last edited" timestamp. Stamped automatically on every insert/update in AppDbContext.SaveChanges.
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
}
