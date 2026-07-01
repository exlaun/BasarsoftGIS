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
    }

    // Shared mapping for a geometry table: explicit table name (so snake_case doesn't pluralize it),
    // the PostGIS column type + SRID, the owner FK to users, and the same soft-delete filter.
    private static void ConfigureGeometry<T>(ModelBuilder modelBuilder, string tableName, string geomType)
        where T : class, IGeoFeature
    {
        var entity = modelBuilder.Entity<T>();
        entity.ToTable(tableName);
        entity.Property(x => x.Geom).HasColumnType(geomType);
        entity.HasQueryFilter(x => !x.IsDeleted);
        entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
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
