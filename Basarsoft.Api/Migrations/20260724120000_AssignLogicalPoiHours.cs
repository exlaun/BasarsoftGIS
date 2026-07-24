using Basarsoft.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basarsoft.Api.Migrations;

// Backfills already-seeded databases without requiring the destructive demo reseed. The model stays
// nullable for external data; only existing incomplete rows receive the logical category schedule.
[DbContext(typeof(AppDbContext))]
[Migration("20260724120000_AssignLogicalPoiHours")]
public class AssignLogicalPoiHours : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE tbl_poi AS poi
            SET open_time = schedule.open_time,
                close_time = schedule.close_time
            FROM tbl_poi_category AS category,
                 (VALUES
                    ('Hospital',         TIME '00:00', TIME '23:59'),
                    ('Pharmacy',         TIME '08:30', TIME '19:00'),
                    ('24/7 Pharmacy',    TIME '00:00', TIME '23:59'),
                    ('Restaurant',       TIME '10:00', TIME '23:00'),
                    ('Cafe',             TIME '08:00', TIME '23:00'),
                    ('Bakery',           TIME '06:00', TIME '21:00'),
                    ('Fast Food',        TIME '10:00', TIME '23:59'),
                    ('Mall',             TIME '10:00', TIME '22:00'),
                    ('Supermarket',      TIME '08:00', TIME '22:00'),
                    ('Hotel',            TIME '00:00', TIME '23:59'),
                    ('Historical Site',  TIME '09:00', TIME '18:00'),
                    ('Museum',           TIME '09:00', TIME '18:00'),
                    ('Art Gallery',      TIME '10:00', TIME '19:00'),
                    ('Visitor Center',   TIME '09:00', TIME '18:00'),
                    ('Bank',             TIME '09:00', TIME '17:00'),
                    ('Gas Station',      TIME '00:00', TIME '23:59'),
                    ('Post Office',      TIME '08:30', TIME '17:30'),
                    ('Municipality',     TIME '08:30', TIME '17:30'),
                    ('Airport',          TIME '00:00', TIME '23:59'),
                    ('Train Station',    TIME '00:00', TIME '23:59'),
                    ('Bus Terminal',     TIME '00:00', TIME '23:59'),
                    ('Ferry Terminal',   TIME '05:30', TIME '23:30'),
                    ('Metro Station',    TIME '06:00', TIME '23:59'),
                    ('University',       TIME '08:00', TIME '20:00'),
                    ('Library',          TIME '09:00', TIME '20:00'),
                    ('High School',      TIME '08:00', TIME '17:00'),
                    ('National Park',    TIME '06:00', TIME '20:00'),
                    ('Beach',            TIME '06:00', TIME '20:00'),
                    ('Park',             TIME '06:00', TIME '23:00'),
                    ('Botanical Garden', TIME '08:00', TIME '20:00'),
                    ('Stadium',          TIME '08:00', TIME '22:00'),
                    ('Ski Center',       TIME '08:00', TIME '17:00'),
                    ('Gym',              TIME '06:00', TIME '23:00')
                 ) AS schedule(category_name, open_time, close_time)
            WHERE poi.category_id = category.id
              AND category.name = schedule.category_name
              AND (poi.open_time IS NULL OR poi.close_time IS NULL);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally no-op: after backfill there is no safe way to distinguish an inferred value
        // from an identical genuine value without destroying user-maintained hours.
    }
}
