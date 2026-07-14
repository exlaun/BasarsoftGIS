using Microsoft.EntityFrameworkCore;
using Basarsoft.Api.Models;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

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

    // RBAC: roles + permissions (the shared permission list) and the three many-to-many link tables.
    public DbSet<Role> Roles { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<UserRole> UserRoles { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<UserPermission> UserPermissions { get; set; }

    // Geographic authorization areas: one polygon per user or role limiting where they may draw.
    public DbSet<GeoAuthorization> GeoAuthorizations { get; set; }

    // POI module: the shared points-of-interest catalogue and its parent-child category tree.
    public DbSet<Poi> Pois { get; set; }
    public DbSet<PoiCategory> PoiCategories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PostGIS is the PostgreSQL add-on that provides the geometry column type + spatial functions.
        // Declaring it here makes EF emit "CREATE EXTENSION postgis" in the migration.
        modelBuilder.HasPostgresExtension("postgis");

        // Every table gets its id from a named per-table sequence (seq_<table>) instead of an implicit
        // identity column, so the sequences are visible, inspectable objects in the database.
        foreach (var table in new[]
                 {
                     "users", "roles", "permissions",
                     "tbl_point", "tbl_line", "tbl_polygon",
                     "user_roles", "role_permissions", "user_permissions",
                     "tbl_geo_authorization",
                     "tbl_poi_category", "tbl_poi",
                 })
        {
            modelBuilder.HasSequence<int>($"seq_{table}");
        }

        // Soft-deleted users disappear from every query automatically (no need to add WHERE clauses).
        modelBuilder.Entity<User>().HasQueryFilter(u => !u.IsDeleted);
        UseSequenceId<User>(modelBuilder, "users");

        // The three drawing tables share the same shape; only the table name + geometry type differ.
        ConfigureGeometry<PointFeature>(modelBuilder, "tbl_point", "geometry(Point,4326)");
        ConfigureGeometry<LineFeature>(modelBuilder, "tbl_line", "geometry(LineString,4326)");
        ConfigureGeometry<PolygonFeature>(modelBuilder, "tbl_polygon", "geometry(Polygon,4326)");

        ConfigureRbac(modelBuilder);
        ConfigureGeoAuthorization(modelBuilder);
        ConfigurePoi(modelBuilder);
    }

    // POI module tables. The category tree is a self-referencing table (parent_id -> same table);
    // Restrict FKs are a DB backstop — the real "no orphans" rule lives in PoiCategoryService, which
    // refuses to delete a category that still has children or POIs (soft deletes never fire the FK).
    private static void ConfigurePoi(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PoiCategory>(e =>
        {
            e.ToTable("tbl_poi_category");
            e.HasQueryFilter(c => !c.IsDeleted);
            e.HasOne<PoiCategory>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(c => c.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(c => c.ModifiedUserId);
            e.HasIndex(c => c.ParentId);
        });
        UseSequenceId<PoiCategory>(modelBuilder, "tbl_poi_category");

        // Poi is an IGeoFeature, so the shared geometry mapping applies as-is; only the category FK
        // and an index on it are extra.
        ConfigureGeometry<Poi>(modelBuilder, "tbl_poi", "geometry(Point,4326)");
        modelBuilder.Entity<Poi>(e =>
        {
            e.HasOne<PoiCategory>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.CategoryId);
        });
    }

    // Geographic authorization areas: each row belongs to exactly ONE user or ONE
    // role (check constraint), holds a single polygon, and at most one live row exists per target
    // (partial unique indexes skip soft-deleted rows so history can accumulate).
    private static void ConfigureGeoAuthorization(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GeoAuthorization>(e =>
        {
            e.ToTable("tbl_geo_authorization", t =>
                t.HasCheckConstraint("ck_tbl_geo_authorization_target", "num_nonnulls(user_id, role_id) = 1"));
            e.Property(x => x.Geom).HasColumnType("geometry(Polygon,4326)");
            e.HasQueryFilter(x => !x.IsDeleted);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("user_id IS NOT NULL AND NOT is_deleted");
            e.HasIndex(x => x.RoleId).IsUnique().HasFilter("role_id IS NOT NULL AND NOT is_deleted");
        });
        UseSequenceId<GeoAuthorization>(modelBuilder, "tbl_geo_authorization");
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
        UseSequenceId<Role>(modelBuilder, "roles");

        modelBuilder.Entity<Permission>(e =>
        {
            e.ToTable("permissions");
            e.HasQueryFilter(p => !p.IsDeleted);
            e.HasIndex(p => p.Name).IsUnique();
        });
        UseSequenceId<Permission>(modelBuilder, "permissions");

        // user_roles: which roles a user holds. Surrogate sequence-backed id; the old composite key
        // lives on as a unique index so the same pair can never be linked twice.
        modelBuilder.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        UseSequenceId<UserRole>(modelBuilder, "user_roles");

        // role_permissions: which permissions a role grants.
        modelBuilder.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });
        UseSequenceId<RolePermission>(modelBuilder, "role_permissions");

        // user_permissions: permissions granted directly to a user, independent of any role.
        modelBuilder.Entity<UserPermission>(e =>
        {
            e.ToTable("user_permissions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.PermissionId }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });
        UseSequenceId<UserPermission>(modelBuilder, "user_permissions");
    }

    // Point an entity's Id at its per-table sequence (default nextval) and switch off the provider's
    // identity-column convention so the column really is plain int + sequence default in the database.
    private static void UseSequenceId<T>(ModelBuilder modelBuilder, string tableName) where T : class
    {
        modelBuilder.Entity<T>().Property<int>("Id")
            .HasDefaultValueSql($"nextval('seq_{tableName}')")
            .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.None);
    }

    // Shared mapping for a geometry table: explicit table name (so snake_case doesn't pluralize it),
    // the PostGIS column type + SRID, the owner FK to users, and the same soft-delete filter.
    private static void ConfigureGeometry<T>(ModelBuilder modelBuilder, string tableName, string geomType)
        where T : class, IGeoFeature
    {
        var entity = modelBuilder.Entity<T>();
        entity.ToTable(tableName);
        UseSequenceId<T>(modelBuilder, tableName);
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
