using System.Text.Json;
using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.IO.Converters;

namespace Basarsoft.Api.Data;

// Loads Turkey's 81 province boundaries into tbl_province from Data/provinces.geojson (simplified
// OSM admin_level=4 data, © OpenStreetMap contributors, ODbL — see geoserver/README.md for the
// download + mapshaper pipeline that produced the file). Idempotent: a non-empty table means the
// reference data is already in place and the seeder no-ops, mirroring AdminSeeder's guards.
public static class ProvinceSeeder
{
    private const int Srid = 4326;

    // GeoJSON4STJ converters so System.Text.Json can turn a GeoJSON geometry into an NTS Geometry
    // (same setup as GeoServerReadService's WFS parsing).
    private static readonly JsonSerializerOptions GeoJsonOptions = CreateGeoJsonOptions();

    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Provinces.AnyAsync())
            return;

        var path = Path.Combine(AppContext.BaseDirectory, "Data", "provinces.geojson");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        if (!doc.RootElement.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("provinces.geojson is not a GeoJSON FeatureCollection.");
        }

        foreach (var feature in features.EnumerateArray())
        {
            var name = feature.GetProperty("properties").GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var geometry = JsonSerializer.Deserialize<Geometry>(
                feature.GetProperty("geometry").GetRawText(), GeoJsonOptions);
            if (geometry is null || geometry.IsEmpty)
                continue;

            // Simplification can leave slightly invalid rings behind; fix them once here so every
            // later ST_Intersects (analysis clip, matched-POI count) runs on clean geometry.
            if (!geometry.IsValid)
                geometry = GeometryFixer.Fix(geometry);

            // The column is geometry(MultiPolygon,4326); most simplified provinces come out as plain
            // Polygons, so wrap those. GeometryFixer may also return a Polygon for a MultiPolygon input.
            if (geometry is Polygon polygon)
                geometry = polygon.Factory.CreateMultiPolygon(new[] { polygon });

            if (geometry is not MultiPolygon)
                throw new InvalidOperationException($"Province '{name}' is not a (Multi)Polygon.");

            geometry.SRID = Srid;

            db.Provinces.Add(new Province
            {
                Name = name,
                Geom = geometry,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private static JsonSerializerOptions CreateGeoJsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new GeoJsonConverterFactory());
        return options;
    }
}
