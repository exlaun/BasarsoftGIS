using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Basarsoft.Api.Controllers;

// Turkey's 81 provinces — static reference data for the location-analysis region dropdown. Readable
// by every authenticated user (the tool must work for the permission-free Viewer role too); there are
// no writes, and ProvinceSeeder synchronizes the validated catalog at startup. Thin enough to query
// the context directly, like the reference reads elsewhere.
[ApiController]
[Authorize]
[Route("api/provinces")]
public class ProvinceController : ControllerBase
{
    // The catalog is synchronized before the app accepts requests and province reference data has no
    // runtime write path. Retaining the materialized DTOs avoids repeatedly converting roughly
    // 310,000 boundary coordinates to WKT for every nationwide map load.
    private const string MapCacheKey = "province-map-response-v2";
    private static readonly SemaphoreSlim MapCacheLock = new(1, 1);

    private readonly AppDbContext _db;
    private readonly IProvinceCatalog _catalog;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ProvinceController> _logger;

    public ProvinceController(
        AppDbContext db,
        IProvinceCatalog catalog,
        IMemoryCache cache,
        ILogger<ProvinceController> logger)
    {
        _db = db;
        _catalog = catalog;
        _cache = cache;
        _logger = logger;
    }

    // GET /api/provinces/map -> all 81 boundary/capital pairs. The persisted province row remains
    // authoritative for ids and boundaries; region/color/capital metadata comes from the validated,
    // versioned source catalog used by ProvinceSeeder.
    [HttpGet("map")]
    public async Task<ActionResult<IReadOnlyList<ProvinceMapResponse>>> Map(
        CancellationToken cancellationToken)
    {
        try
        {
            var cached = await GetMapResponseAsync(cancellationToken);
            // Retain the response in the authenticated caller's cache, but require a conditional
            // check. This avoids a multi-megabyte repeat transfer without allowing a shared cache
            // to satisfy an otherwise-authorized request.
            Response.Headers.CacheControl = "private, no-cache";
            Response.Headers.ETag = cached.ETag;
            if (MatchesEntityTag(Request.Headers.IfNoneMatch.ToString(), cached.ETag))
                return StatusCode(StatusCodes.Status304NotModified);

            return Ok(cached.Response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Map));
        }
    }

    private static bool MatchesEntityTag(string header, string currentTag) =>
        header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate =>
                candidate == "*" ||
                string.Equals(
                    candidate.StartsWith("W/", StringComparison.Ordinal) ? candidate[2..] : candidate,
                    currentTag,
                    StringComparison.Ordinal));

    private async Task<MapResponseCacheEntry> GetMapResponseAsync(
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<MapResponseCacheEntry>(MapCacheKey, out var cached))
            return cached!;

        // IMemoryCache does not serialize concurrent factory calls. This lock prevents a burst of
        // authenticated map loads from each paying the full database read and WKT conversion cost.
        await MapCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<MapResponseCacheEntry>(MapCacheKey, out cached))
                return cached!;

            var catalog = await _catalog.GetAsync(cancellationToken);
            var metadata = catalog.ToDictionary(entry => entry.Name, StringComparer.Ordinal);
            var provinces = await _db.Provinces
                .AsNoTracking()
                .OrderBy(province => province.Name)
                .ToListAsync(cancellationToken);

            if (provinces.Count != ProvinceCatalog.ExpectedProvinceCount)
            {
                throw new InvalidOperationException(
                    $"Expected {ProvinceCatalog.ExpectedProvinceCount} province rows; found {provinces.Count}.");
            }

            var response = new List<ProvinceMapResponse>(provinces.Count);
            foreach (var province in provinces)
            {
                if (!metadata.TryGetValue(province.Name, out var entry))
                {
                    throw new InvalidOperationException(
                        $"Province '{province.Name}' is missing from the source catalog.");
                }

                response.Add(new ProvinceMapResponse
                {
                    Id = province.Id,
                    Name = province.Name,
                    Region = entry.Region,
                    Color = entry.Color,
                    BoundaryWkt = province.Geom.AsText(),
                    CapitalName = entry.CapitalName,
                    CapitalWkt = entry.CapitalGeom.AsText(),
                });
            }

            var cachedResponse = response.AsReadOnly();
            cached = new MapResponseCacheEntry(
                cachedResponse,
                $"\"{CreateEntityTag(cachedResponse)}\"");
            _cache.Set(MapCacheKey, cached, new MemoryCacheEntryOptions
            {
                // The cache is intentionally bounded by process lifetime: startup seeding creates
                // the immutable snapshot and a process restart naturally picks up a new catalog.
                Priority = CacheItemPriority.NeverRemove,
            });
            return cached;
        }
        finally
        {
            MapCacheLock.Release();
        }
    }

    private static string CreateEntityTag(IReadOnlyList<ProvinceMapResponse> response)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var province in response)
        {
            AppendHashValue(hash, province.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendHashValue(hash, province.Name);
            AppendHashValue(hash, province.Region);
            AppendHashValue(hash, province.Color);
            AppendHashValue(hash, province.BoundaryWkt);
            AppendHashValue(hash, province.CapitalName);
            AppendHashValue(hash, province.CapitalWkt);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendHashValue(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private sealed record MapResponseCacheEntry(
        IReadOnlyList<ProvinceMapResponse> Response,
        string ETag);

    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in ProvinceController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }

    // GET /api/provinces -> id + name only; the boundary comes per province from the detail endpoint,
    // so the dropdown load stays a few hundred bytes instead of the whole 81-polygon geometry set.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProvinceResponse>>> List()
    {
        try
        {
            var provinces = await _db.Provinces
                .OrderBy(p => p.Name)
                .Select(p => new ProvinceResponse { Id = p.Id, Name = p.Name })
                .ToListAsync();
            return Ok(provinces);
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(List));
        }
    }

    // GET /api/provinces/{id} -> the boundary as WKT, fetched when the user picks a province so the
    // map can draw the region outline and the analysis request can echo what was chosen.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProvinceDetailResponse>> Get(int id)
    {
        try
        {
            var province = await _db.Provinces
                .Where(p => p.Id == id)
                .Select(p => new ProvinceDetailResponse { Id = p.Id, Name = p.Name, Wkt = p.Geom.AsText() })
                .FirstOrDefaultAsync();

            if (province is null)
                return NotFound(new { message = "Province not found." });

            return Ok(province);
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Get));
        }
    }
}
