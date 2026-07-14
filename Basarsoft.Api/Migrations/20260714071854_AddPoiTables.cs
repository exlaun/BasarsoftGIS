using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPoiTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "seq_tbl_poi");

            migrationBuilder.CreateSequence<int>(
                name: "seq_tbl_poi_category");

            migrationBuilder.CreateTable(
                name: "tbl_poi_category",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('seq_tbl_poi_category')"),
                    name = table.Column<string>(type: "text", nullable: false),
                    parent_id = table.Column<int>(type: "integer", nullable: true),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    modified_user_id = table.Column<int>(type: "integer", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tbl_poi_category", x => x.id);
                    table.ForeignKey(
                        name: "fk_tbl_poi_category_tbl_poi_category_parent_id",
                        column: x => x.parent_id,
                        principalTable: "tbl_poi_category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tbl_poi_category_users_modified_user_id",
                        column: x => x.modified_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tbl_poi_category_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tbl_poi",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('seq_tbl_poi')"),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    color = table.Column<string>(type: "text", nullable: true),
                    geom = table.Column<Geometry>(type: "geometry(Point,4326)", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    open_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    close_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    modified_user_id = table.Column<int>(type: "integer", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tbl_poi", x => x.id);
                    table.ForeignKey(
                        name: "fk_tbl_poi_tbl_poi_category_category_id",
                        column: x => x.category_id,
                        principalTable: "tbl_poi_category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tbl_poi_users_modified_user_id",
                        column: x => x.modified_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tbl_poi_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tbl_poi_category_id",
                table: "tbl_poi",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_poi_modified_user_id",
                table: "tbl_poi",
                column: "modified_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_poi_user_id",
                table: "tbl_poi",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_poi_category_modified_user_id",
                table: "tbl_poi_category",
                column: "modified_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_poi_category_parent_id",
                table: "tbl_poi_category",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_poi_category_user_id",
                table: "tbl_poi_category",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_poi");

            migrationBuilder.DropTable(
                name: "tbl_poi_category");

            migrationBuilder.DropSequence(
                name: "seq_tbl_poi");

            migrationBuilder.DropSequence(
                name: "seq_tbl_poi_category");
        }
    }
}
