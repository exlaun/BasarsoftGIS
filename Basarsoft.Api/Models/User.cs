namespace Basarsoft.Api.Models;

// Represents an application user stored in the database.
public class User
{
    // Primary key. EF Core automatically treats a property named "Id" as the PK.
    public int Id { get; set; }

    // The user's login name.
    public string Username { get; set; } = string.Empty;

    // The hashed password. We never store the plain-text password (hashing comes later with JWT).
    public string PasswordHash { get; set; } = string.Empty;

    // When the user record was created. We use UtcNow because Npgsql stores this as a UTC timestamp.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
