using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace Basarsoft.Api.Services;

public class LocationAnalysisService : ILocationAnalysisService
{
    private readonly AppDbContext _db;
    private readonly WKTReader _wktReader = new();

    // Same storage CRS as every other geometry table: EPSG:4326 (WGS84 lon-lat).
    private const int Srid = 4326;

    public LocationAnalysisService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<LocationAnalysisWriteResult> CreateAsync(LocationAnalysisCreateRequest request, int userId)
    {
        // Exactly one region source. Annotations can't express XOR, so it lives here.
        if ((request.ProvinceId is null) == (string.IsNullOrWhiteSpace(request.RegionWkt)))
            return LocationAnalysisWriteResult.RegionRequired;

        // The criteria list already passed [MinLength(2)]/[MaxLength(5)]/[Range(1,100)], but the rules
        // are re-checked here so the service is safe on its own (the mentor's "must not start" rule
        // is a domain rule, not a transport detail).
        var criteria = request.Criteria;
        if (criteria.Count is < 2 or > 5 ||
            criteria.Any(c => c.CategoryId is null || c.Weight is null or < 1 or > 100))
        {
            return LocationAnalysisWriteResult.WeightSumInvalid;
        }

        var categoryIds = criteria.Select(c => c.CategoryId!.Value).ToList();
        if (categoryIds.Distinct().Count() != categoryIds.Count)
            return LocationAnalysisWriteResult.DuplicateCategory;

        if (criteria.Sum(c => c.Weight!.Value) != 100)
            return LocationAnalysisWriteResult.WeightSumInvalid;

        // The soft-delete query filter hides removed categories, so a stale id fails here too.
        var categoryNames = await _db.PoiCategories
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);
        if (categoryNames.Count != categoryIds.Count)
            return LocationAnalysisWriteResult.CategoryNotFound;

        // Resolve the region to one MultiPolygon, whatever its source.
        Province? province = null;
        Geometry region;
        if (request.ProvinceId is int provinceId)
        {
            province = await _db.Provinces.FirstOrDefaultAsync(p => p.Id == provinceId);
            if (province is null)
                return LocationAnalysisWriteResult.ProvinceNotFound;
            region = province.Geom;
        }
        else
        {
            // Parse the WKT the client drew. Bad text -> InvalidGeometry -> 400 (PoiService idiom).
            Geometry parsed;
            try
            {
                parsed = _wktReader.Read(request.RegionWkt);
            }
            catch
            {
                return LocationAnalysisWriteResult.InvalidGeometry;
            }

            if (parsed is null || parsed.IsEmpty || !parsed.IsValid ||
                parsed.OgcGeometryType is not (OgcGeometryType.Polygon or OgcGeometryType.MultiPolygon))
            {
                return LocationAnalysisWriteResult.InvalidGeometry;
            }

            if (parsed is Polygon polygon)
                parsed = polygon.Factory.CreateMultiPolygon(new[] { polygon });

            parsed.SRID = Srid;
            region = parsed;
        }

        var analysis = new LocationAnalysis
        {
            UserId = userId,
            Geom = region,
            ProvinceId = province?.Id,
            CreatedAt = DateTime.UtcNow,
            // A never-edited run reports its creator as the last modifier, like the geometry tables.
            ModifiedUserId = userId,
        };

        // Two saves (the criteria rows need the run id, which the sequence only hands out on insert)
        // under one transaction, so a failure can't leave a criterion-less run behind.
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            _db.LocationAnalyses.Add(analysis);
            await _db.SaveChangesAsync();

            foreach (var criterion in criteria)
            {
                _db.LocationAnalysisCriteria.Add(new LocationAnalysisCriterion
                {
                    AnalysisId = analysis.Id,
                    CategoryId = criterion.CategoryId!.Value,
                    Weight = criterion.Weight!.Value,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedUserId = userId,
                });
            }
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();
        }

        var matchedPoiCount = await CountMatchedPoisAsync(categoryIds, region);

        return LocationAnalysisWriteResult.Ok(new LocationAnalysisResponse
        {
            Id = analysis.Id,
            ProvinceId = province?.Id,
            ProvinceName = province?.Name,
            RegionWkt = region.AsText(),
            MatchedPoiCount = matchedPoiCount,
            Criteria = criteria.Select(c => new LocationAnalysisCriterionResponse
            {
                CategoryId = c.CategoryId!.Value,
                CategoryName = categoryNames[c.CategoryId!.Value],
                Weight = c.Weight!.Value,
            }).ToList(),
            CreatedAt = analysis.CreatedAt,
        });
    }

    public Task<bool> IsOwnedAsync(int id, int userId) =>
        // Ownership sits in the WHERE clause so "not yours" == "doesn't exist" (no information leak);
        // the soft-delete query filter already excludes removed runs.
        _db.LocationAnalyses.AnyAsync(a => a.Id == id && a.UserId == userId);

    // How many live POIs inside the region match at least one criterion. A criterion on a parent
    // category matches the whole subtree, so expand each id to its descendants in memory first (the
    // category table is small; same walk PoiService uses for breadcrumbs) and count distinct POIs.
    private async Task<int> CountMatchedPoisAsync(IReadOnlyList<int> categoryIds, Geometry region)
    {
        var categories = await _db.PoiCategories
            .Select(c => new { c.Id, c.ParentId })
            .ToListAsync();

        var childrenByParent = categories
            .Where(c => c.ParentId is not null)
            .ToLookup(c => c.ParentId!.Value, c => c.Id);

        var matchedIds = new HashSet<int>();
        foreach (var rootId in categoryIds)
        {
            var queue = new Queue<int>();
            queue.Enqueue(rootId);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!matchedIds.Add(id))
                    continue; // already expanded (overlapping criteria subtrees / defensive on cycles)
                foreach (var childId in childrenByParent[id])
                    queue.Enqueue(childId);
            }
        }

        // ST_Intersects keeps boundary POIs in, matching the Covers semantics used elsewhere.
        return await _db.Pois.CountAsync(p => matchedIds.Contains(p.CategoryId) && p.Geom.Intersects(region));
    }
}
