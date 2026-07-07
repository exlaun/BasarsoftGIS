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

    // Each DbSet becomes a table.
    public DbSet<User> Users { get; set; }
    public DbSet<PointFeature> Points { get; set; }
    public DbSet<LineFeature> Lines { get; set; }
    public DbSet<PolygonFeature> Polygons { get; set; }

    // RBAC: roles + permissions (the shared "yetki" list) and the three many-to-many link tables.
    public DbSet<Role> Roles { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<UserRole> UserRoles { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<UserPermission> UserPermissions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PostGIS is the PostgreSQL add-on that provides the geometry column type + spatial functions.
        // Declaring it here makes EF emit "CREATE EXTENSION postgis" in the migration.
        modelBuilder.HasPostgresExtension("postgis");

        // Soft-deleted users disappear from every query automatically (no need to add WHERE clauses).
        modelBuilder.Entity<User>().HasQueryFilter(u => !u.IsDeleted);

        // The three drawing tables share the same shape; only the table name + geometry type differ.
        ConfigureGeometry<PointFeature>(modelBuilder, "tbl_point", "geometry(Point,4326)");
        ConfigureGeometry<LineFeature>(modelBuilder, "tbl_line", "geometry(LineString,4326)");
        ConfigureGeometry<PolygonFeature>(modelBuilder, "tbl_polygon", "geometry(Polygon,4326)");

        ConfigureRbac(modelBuilder);
    }

    // Roles/permissions and their many-to-many links. Roles + permissions get the same soft-delete
    // filter as users; the join tables are deliberately filter-free (effective-permission queries reach
    // the deleted state by joining back through the filtered Roles/Permissions sets instead).
    private static void ConfigureRbac(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.HasQueryFilter(r => !r.IsDeleted);
            e.HasIndex(r => r.Name).IsUnique();
        });

        modelBuilder.Entity<Permission>(e =>
        {
            e.ToTable("permissions");
            e.HasQueryFilter(p => !p.IsDeleted);
            e.HasIndex(p => p.Name).IsUnique();
        });

        // user_roles: which roles a user holds.
        modelBuilder.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles");
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        // role_permissions: which permissions a role grants.
        modelBuilder.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions");
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        // user_permissions: permissions granted directly to a user, independent of any role.
        modelBuilder.Entity<UserPermission>(e =>
        {
            e.ToTable("user_permissions");
            e.HasKey(x => new { x.UserId, x.PermissionId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    // Shared mapping for a geometry table: explicit table name (so snake_case doesn't pluralize it),
    // the PostGIS column type + SRID, the owner FK to users, and the same soft-delete filter.
    private static void ConfigureGeometry<T>(ModelBuilder modelBuilder, string tableName, string geomType)
        where T : class, IGeoFeature
    {
        var entity = modelBuilder.Entity<T>();
        entity.ToTable(tableName);
        entity.Property(x => x.Geom).HasColumnType(geomType);
        // Hide both soft-deleted and deactivated shapes from every query automatically.
        entity.HasQueryFilter(x => !x.IsDeleted && x.IsActive);
        entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        // Second FK to users: who last modified the shape (nullable — legacy rows predate the column).
        entity.HasOne<User>().WithMany().HasForeignKey(x => x.ModifiedUserId);
    }

    // Stamp ModifiedDate on every insert/update so no service ever forgets to.
    public override int SaveChanges()
    {
        StampModifiedDates();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampModifiedDates();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampModifiedDates()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.ModifiedDate = now;
        }
    }
}
