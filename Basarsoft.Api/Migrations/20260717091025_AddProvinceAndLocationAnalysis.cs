using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProvinceAndLocationAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "seq_tbl_location_analysis");

            migrationBuilder.CreateSequence<int>(
                name: "seq_tbl_location_analysis_criterion");

            migrationBuilder.CreateSequence<int>(
                name: "seq_tbl_province");

            migrationBuilder.CreateTable(
                name: "tbl_province",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('seq_tbl_province')"),
                    name = table.Column<string>(type: "text", nullable: false),
                    geom = table.Column<Geometry>(type: "geometry(MultiPolygon,4326)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tbl_province", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tbl_location_analysis",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('seq_tbl_location_analysis')"),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    geom = table.Column<Geometry>(type: "geometry(MultiPolygon,4326)", nullable: false),
                    province_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    modified_user_id = table.Column<int>(type: "integer", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tbl_location_analysis", x => x.id);
                    table.ForeignKey(
                        name: "fk_tbl_location_analysis_tbl_province_province_id",
                        column: x => x.province_id,
                        principalTable: "tbl_province",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tbl_location_analysis_users_modified_user_id",
                        column: x => x.modified_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tbl_location_analysis_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tbl_location_analysis_criterion",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('seq_tbl_location_analysis_criterion')"),
                    analysis_id = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    weight = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    modified_user_id = table.Column<int>(type: "integer", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tbl_location_analysis_criterion", x => x.id);
                    table.CheckConstraint("ck_tbl_location_analysis_criterion_weight", "weight BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "fk_tbl_location_analysis_criterion_tbl_location_analysis_analy",
                        column: x => x.analysis_id,
                        principalTable: "tbl_location_analysis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tbl_location_analysis_criterion_tbl_poi_category_category_id",
                        column: x => x.category_id,
                        principalTable: "tbl_poi_category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tbl_location_analysis_criterion_users_modified_user_id",
                        column: x => x.modified_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tbl_location_analysis_modified_user_id",
                table: "tbl_location_analysis",
                column: "modified_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_location_analysis_province_id",
                table: "tbl_location_analysis",
                column: "province_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_location_analysis_user_id",
                table: "tbl_location_analysis",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_location_analysis_criterion_analysis_id_category_id",
                table: "tbl_location_analysis_criterion",
                columns: new[] { "analysis_id", "category_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tbl_location_analysis_criterion_category_id",
                table: "tbl_location_analysis_criterion",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_location_analysis_criterion_modified_user_id",
                table: "tbl_location_analysis_criterion",
                column: "modified_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_province_name",
                table: "tbl_province",
                column: "name",
                unique: true);

            // Spatial (GiST) indexes for the ST_Intersects clip in vw_konum and the matched-POI count;
            // EF's CreateIndex can't emit USING GIST, so raw SQL. They drop with their tables in Down().
            migrationBuilder.Sql("CREATE INDEX ix_tbl_province_geom ON tbl_province USING GIST (geom);");
            migrationBuilder.Sql("CREATE INDEX ix_tbl_location_analysis_geom ON tbl_location_analysis USING GIST (geom);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_location_analysis_criterion");

            migrationBuilder.DropTable(
                name: "tbl_location_analysis");

            migrationBuilder.DropTable(
                name: "tbl_province");

            migrationBuilder.DropSequence(
                name: "seq_tbl_location_analysis");

            migrationBuilder.DropSequence(
                name: "seq_tbl_location_analysis_criterion");

            migrationBuilder.DropSequence(
                name: "seq_tbl_province");
        }
    }
}
