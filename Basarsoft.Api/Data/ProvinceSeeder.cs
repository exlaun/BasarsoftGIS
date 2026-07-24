using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Basarsoft.Api.Data;

// Synchronizes Turkey's exact 81 province boundaries from the validated source-backed catalog.
// Existing ids are preserved (location-analysis rows reference them); changed boundaries are updated,
// missing rows are inserted, and unknown extra rows fail fast rather than leaving a partial catalogue.
public static class ProvinceSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        IProvinceCatalog? catalog = null,
        CancellationToken cancellationToken = default)
    {
        catalog ??= new ProvinceCatalog();
        var entries = await catalog.GetAsync(cancellationToken);
        var existing = await db.Provinces.ToListAsync(cancellationToken);
        var sourceNames = entries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        var unexpected = existing
            .Where(province => !sourceNames.Contains(province.Name))
            .Select(province => province.Name)
            .OrderBy(name => name)
            .ToArray();
        if (unexpected.Length > 0)
        {
            throw new InvalidOperationException(
                $"tbl_province contains names absent from provinces.geojson: {string.Join(", ", unexpected)}.");
        }

        var byName = existing.ToDictionary(province => province.Name, StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var entry in entries)
        {
            if (!byName.TryGetValue(entry.Name, out var province))
            {
                db.Provinces.Add(new Province
                {
                    Name = entry.Name,
                    Geom = entry.Boundary.Copy(),
                    CreatedAt = now,
                    ModifiedDate = now,
                });
                changed = true;
                continue;
            }

            if (province.Geom.SRID != entry.Boundary.SRID ||
                !province.Geom.EqualsExact(entry.Boundary))
            {
                province.Geom = entry.Boundary.Copy();
                province.ModifiedDate = now;
                changed = true;
            }
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken);
    }
}
