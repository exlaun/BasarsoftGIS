using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    public partial class AllowUnknownPoiHoursAndMultiPolygonGeoAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<TimeOnly>(
                name: "open_time",
                table: "tbl_poi",
                type: "time without time zone",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone");

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "close_time",
                table: "tbl_poi",
                type: "time without time zone",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone");

            // PostgreSQL cannot infer this typmod conversion. Every legacy value is a Polygon, so
            // ST_Multi is lossless and gives the column one canonical storage type.
            migrationBuilder.Sql("""
                ALTER TABLE tbl_geo_authorization
                ALTER COLUMN geom TYPE geometry(MultiPolygon,4326)
                USING ST_Multi(geom)::geometry(MultiPolygon,4326);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Unknown imported hours have no representation in the old schema. Keep the historic
            // midnight fallback explicit so SET NOT NULL cannot fail halfway through a rollback.
            migrationBuilder.Sql("""
                UPDATE tbl_poi SET open_time = TIME '00:00:00' WHERE open_time IS NULL;
                UPDATE tbl_poi SET close_time = TIME '00:00:00' WHERE close_time IS NULL;
                """);

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "open_time",
                table: "tbl_poi",
                type: "time without time zone",
                nullable: false,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "close_time",
                table: "tbl_poi",
                type: "time without time zone",
                nullable: false,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone",
                oldNullable: true);

            // Refuse to silently discard disconnected components on downgrade. Single-component
            // MultiPolygons round-trip losslessly to the legacy Polygon typmod.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM tbl_geo_authorization
                        WHERE ST_NumGeometries(geom) <> 1
                    ) THEN
                        RAISE EXCEPTION
                            'Cannot downgrade geo authorization: a MultiPolygon has multiple components.';
                    END IF;
                END $$;

                ALTER TABLE tbl_geo_authorization
                ALTER COLUMN geom TYPE geometry(Polygon,4326)
                USING ST_GeometryN(geom, 1)::geometry(Polygon,4326);
                """);
        }
    }
}
