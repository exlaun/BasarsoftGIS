using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTransportationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "seq_tbl_route");

            migrationBuilder.CreateSequence<int>(
                name: "seq_tbl_stop");

            migrationBuilder.CreateTable(
                name: "tbl_route",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('seq_tbl_route')"),
                    name = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    modified_user_id = table.Column<int>(type: "integer", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tbl_route", x => x.id);
                    table.ForeignKey(
                        name: "fk_tbl_route_users_modified_user_id",
                        column: x => x.modified_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tbl_route_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tbl_stop",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('seq_tbl_stop')"),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    color = table.Column<string>(type: "text", nullable: true),
                    geom = table.Column<Geometry>(type: "geometry(Point,4326)", nullable: false),
                    route_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    modified_user_id = table.Column<int>(type: "integer", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tbl_stop", x => x.id);
                    table.ForeignKey(
                        name: "fk_tbl_stop_tbl_route_route_id",
                        column: x => x.route_id,
                        principalTable: "tbl_route",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tbl_stop_users_modified_user_id",
                        column: x => x.modified_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tbl_stop_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tbl_route_modified_user_id",
                table: "tbl_route",
                column: "modified_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_route_user_id",
                table: "tbl_route",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_stop_modified_user_id",
                table: "tbl_stop",
                column: "modified_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_stop_route_id",
                table: "tbl_stop",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_stop_route_id_sequence_order",
                table: "tbl_stop",
                columns: new[] { "route_id", "sequence_order" });

            migrationBuilder.CreateIndex(
                name: "ix_tbl_stop_user_id",
                table: "tbl_stop",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_stop");

            migrationBuilder.DropTable(
                name: "tbl_route");

            migrationBuilder.DropSequence(
                name: "seq_tbl_route");

            migrationBuilder.DropSequence(
                name: "seq_tbl_stop");
        }
    }
}
