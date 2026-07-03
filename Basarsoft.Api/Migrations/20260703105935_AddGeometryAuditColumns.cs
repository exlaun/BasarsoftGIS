using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGeometryAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited backfills (same pattern as the users audit migration): existing shapes must
            // stay enabled, so is_active backfills to true; modified_date backfills to now() instead of
            // 0001-01-01. Without these edits every pre-existing shape would become inactive (hidden by
            // the new query filter) and carry a year-0001 timestamp.
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "tbl_polygon",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "modified_date",
                table: "tbl_polygon",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "tbl_point",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "modified_date",
                table: "tbl_point",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "tbl_line",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "modified_date",
                table: "tbl_line",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            // The now() default above stamps every existing row with the migration-run instant, which
            // would misreport an untouched shape as "last edited just now". Seed modified_date from each
            // row's own created_at so a never-edited shape reports its creation time instead.
            migrationBuilder.Sql("UPDATE tbl_point SET modified_date = created_at;");
            migrationBuilder.Sql("UPDATE tbl_line SET modified_date = created_at;");
            migrationBuilder.Sql("UPDATE tbl_polygon SET modified_date = created_at;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                table: "tbl_polygon");

            migrationBuilder.DropColumn(
                name: "modified_date",
                table: "tbl_polygon");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "tbl_point");

            migrationBuilder.DropColumn(
                name: "modified_date",
                table: "tbl_point");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "tbl_line");

            migrationBuilder.DropColumn(
                name: "modified_date",
                table: "tbl_line");
        }
    }
}
