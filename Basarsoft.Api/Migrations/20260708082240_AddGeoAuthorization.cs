using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGeoAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "seq_tbl_geo_authorization");

            migrationBuilder.CreateTable(
                name: "tbl_geo_authorization",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('seq_tbl_geo_authorization')"),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    role_id = table.Column<int>(type: "integer", nullable: true),
                    geom = table.Column<Geometry>(type: "geometry(Polygon,4326)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tbl_geo_authorization", x => x.id);
                    table.CheckConstraint("ck_tbl_geo_authorization_target", "num_nonnulls(user_id, role_id) = 1");
                    table.ForeignKey(
                        name: "fk_tbl_geo_authorization_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tbl_geo_authorization_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tbl_geo_authorization_role_id",
                table: "tbl_geo_authorization",
                column: "role_id",
                unique: true,
                filter: "role_id IS NOT NULL AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_geo_authorization_user_id",
                table: "tbl_geo_authorization",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL AND NOT is_deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_geo_authorization");

            migrationBuilder.DropSequence(
                name: "seq_tbl_geo_authorization");
        }
    }
}
