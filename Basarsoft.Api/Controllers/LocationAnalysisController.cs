using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

// The "Konum Analizi" (location analysis) tool: POST validates + stores a weighted-criteria run and
// returns its id; GET /{id}/wms renders that run's weighted POI heat map through GeoServer (vw_konum,
// viewparams aid:<id>) exactly like the /api/geometry/wms proxies. Open to every authenticated user —
// the mentor's requirement is that the permission-free User role can run analyses too; the runs
// themselves are private (the WMS proxy only serves an analysis to its owner).
[ApiController]
[Authorize]
[Route("api/location-analysis")]
public class LocationAnalysisController : ControllerBase
{
    private readonly ILocationAnalysisService _analyses;
    private readonly IGeoServerReadService _geoServerReadService;
    private readonly ILogger<LocationAnalysisController> _logger;

    public LocationAnalysisController(
        ILocationAnalysisService analyses,
        IGeoServerReadService geoServerReadService,
        ILogger<LocationAnalysisController> logger)
    {
        _analyses = analyses;
        _geoServerReadService = geoServerReadService;
        _logger = logger;
    }

    // The logged-in user's id from the JWT "sub" claim (same idiom as GeometryController).
    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in LocationAnalysisController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }

    // POST /api/location-analysis -> validate + store a run, return its id and echo data. Every
    // failure is a 400 with a `code` so the client can show a specific Turkish message per rule.
    [HttpPost]
    public async Task<ActionResult<LocationAnalysisResponse>> Create(LocationAnalysisCreateRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            var result = await _analyses.CreateAsync(request, userId);
            return result.Status switch
            {
                LocationAnalysisWriteStatus.RegionRequired => BadRequest(new
                {
                    message = "Send exactly one of provinceId or regionWkt.",
                    code = "region_required",
                }),
                LocationAnalysisWriteStatus.InvalidGeometry => BadRequest(new
                {
                    message = "regionWkt must be a valid Polygon or MultiPolygon.",
                    code = "invalid_geometry",
                }),
                LocationAnalysisWriteStatus.ProvinceNotFound => BadRequest(new
                {
                    message = "Province not found.",
                    code = "province_not_found",
                }),
                LocationAnalysisWriteStatus.DuplicateCategory => BadRequest(new
                {
                    message = "Each category may appear in only one criterion.",
                    code = "duplicate_category",
                }),
                LocationAnalysisWriteStatus.CategoryNotFound => BadRequest(new
                {
                    message = "Category not found.",
                    code = "category_not_found",
                }),
                LocationAnalysisWriteStatus.WeightSumInvalid => BadRequest(new
                {
                    message = "Criterion weights must sum to exactly 100.",
                    code = "weights_sum_invalid",
                }),
                _ => Ok(result.Response),
            };
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Create));
        }
    }

    // GET /api/location-analysis/{id}/wms -> the run's weighted heat map as a PNG. Same contract as
    // /api/geometry/wms/heatmap (client steers bbox/size/crs only); the analysis id is the sole
    // GeoServer parameter, and it is only accepted from the run's owner (404 otherwise — no leak).
    [HttpGet("{id:int}/wms")]
    public async Task<IActionResult> Wms(
        int id,
        [FromQuery] string? bbox,
        [FromQuery] int width,
        [FromQuery] int height,
        [FromQuery] string? crs,
        [FromQuery] string? srs)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            if (!await _analyses.IsOwnedAsync(id, userId))
                return NotFound(new { message = "Analysis not found." });

            // Accept CRS (WMS 1.3.0 vocabulary) or SRS (1.1.1) like the geometry WMS proxies.
            var projection = crs ?? srs;
            if (string.IsNullOrWhiteSpace(bbox) || string.IsNullOrWhiteSpace(projection) || width <= 0 || height <= 0)
                return BadRequest(new { message = "bbox, crs/srs, width and height are required." });

            var image = await _geoServerReadService.GetLocationHeatmapAsync(id, bbox, width, height, projection);
            return File(image.Bytes, image.ContentType);
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Wms));
        }
    }
}
