using Microsoft.EntityFrameworkCore;
using Basarsoft.Api.Models;

namespace Basarsoft.Api.Data;

// The database context is the bridge between your C# classes and the PostgreSQL database.
public class AppDbContext : DbContext
{
    // The options (connection string + provider) are passed in from Program.cs.
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // Each DbSet becomes a table. This maps the User class to a "Users" table.
    public DbSet<User> Users { get; set; }
}
