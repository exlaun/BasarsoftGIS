using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOsrmRouteGeometry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "distance_meters",
                table: "tbl_route",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "duration_seconds",
                table: "tbl_route",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<LineString>(
                name: "geometry",
                table: "tbl_route",
                type: "geometry(LineString,4326)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_geometry_stale",
                table: "tbl_route",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "routing_error_code",
                table: "tbl_route",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            // One-time correction for databases created before Operators became POI read-only.
            // Explicit user_permissions grants are intentionally unaffected.
            migrationBuilder.Sql("""
                DELETE FROM role_permissions rp
                USING roles r, permissions p
                WHERE rp.role_id = r.id
                  AND rp.permission_id = p.id
                  AND r.name = 'Operator'
                  AND p.name = 'add_poi';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "distance_meters",
                table: "tbl_route");

            migrationBuilder.DropColumn(
                name: "duration_seconds",
                table: "tbl_route");

            migrationBuilder.DropColumn(
                name: "geometry",
                table: "tbl_route");

            migrationBuilder.DropColumn(
                name: "is_geometry_stale",
                table: "tbl_route");

            migrationBuilder.DropColumn(
                name: "routing_error_code",
                table: "tbl_route");

            migrationBuilder.Sql("""
                INSERT INTO role_permissions (id, role_id, permission_id)
                SELECT nextval('seq_role_permissions'), r.id, p.id
                FROM roles r
                CROSS JOIN permissions p
                WHERE r.name = 'Operator'
                  AND p.name = 'add_poi'
                  AND NOT EXISTS (
                      SELECT 1 FROM role_permissions rp
                      WHERE rp.role_id = r.id AND rp.permission_id = p.id
                  );
                """);
        }
    }
}
