using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Security;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

// All endpoints require a valid JWT. The {type} route segment is point | line | polygon.
[ApiController]
[Authorize]
[Route("api/geometry")]
public class GeometryController : ControllerBase
{
    private readonly IGeometryService _geometryService;
    private readonly IGeoServerReadService _geoServerReadService;
    private readonly IUserAdminService _userAdminService;
    private readonly ILogger<GeometryController> _logger;

    public GeometryController(
        IGeometryService geometryService,
        IGeoServerReadService geoServerReadService,
        IUserAdminService userAdminService,
        ILogger<GeometryController> logger)
    {
        _geometryService = geometryService;
        _geoServerReadService = geoServerReadService;
        _userAdminService = userAdminService;
        _logger = logger;
    }

    // The logged-in user's id, taken from the JWT "sub" claim (same claim AuthController.Me reads).
    // Returns false for a token whose "sub" is missing or non-numeric so the action can answer 401,
    // rather than throwing (which would surface as a 500).
    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    // Logs an unexpected failure and returns a generic 500 — never the exception text, which could
    // leak connection strings / table names to the client. Each action calls this from its catch.
    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in GeometryController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }

    // GET /api/geometry -> every shape the caller owns, grouped by type (one-shot map load). The data
    // is now pulled from GeoServer's WFS (React -> this API -> GeoServer -> PostGIS), not read from EF
    // directly. userId comes from the JWT and scopes GeoServer's per-user SQL view, so isolation is
    // still enforced server-side. A GeoServer outage surfaces as a 500 (no silent EF fallback).
    [HttpGet]
    public async Task<ActionResult<AllGeometryResponse>> GetAll()
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            return Ok(await _geoServerReadService.GetAllForUserAsync(userId));
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(GetAll));
        }
    }

    // GET /api/geometry/wms -> a single PNG of the caller's shapes rendered by GeoServer's WMS. This is
    // the DISPLAY layer (the WFS/vector path above is the editable one). OpenLayers' ImageWMS sends the
    // current viewport as BBOX/WIDTH/HEIGHT/SRS; the caller only steers the viewport, while the layers
    // and the per-user uid (from the JWT) are fixed server-side. "wms" is a literal segment so it ranks
    // above the {type} route, same as "query".
    [HttpGet("wms")]
    public async Task<IActionResult> Wms(
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

            // OpenLayers sends CRS (WMS 1.3.0) or SRS (1.1.1) depending on version; accept either.
            var projection = string.IsNullOrWhiteSpace(crs) ? srs : crs;
            if (string.IsNullOrWhiteSpace(bbox) || string.IsNullOrWhiteSpace(projection) || width <= 0 || height <= 0)
                return BadRequest(new { message = "bbox, crs/srs, width and height are required." });

            var image = await _geoServerReadService.GetMapAsync(userId, bbox, width, height, projection);
            return File(image.Bytes, image.ContentType);
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Wms));
        }
    }

    // GET /api/geometry/wms/heatmap -> the caller's shape density rendered as a heat map PNG. Same
    // viewport contract as /wms, but GeoServer renders the vw_heat view (all shapes as points) through
    // its vec:Heatmap default style. The legend for this image lives in the frontend (MapPage).
    [HttpGet("wms/heatmap")]
    public async Task<IActionResult> WmsHeatmap(
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

            // OpenLayers sends CRS (WMS 1.3.0) or SRS (1.1.1) depending on version; accept either.
            var projection = string.IsNullOrWhiteSpace(crs) ? srs : crs;
            if (string.IsNullOrWhiteSpace(bbox) || string.IsNullOrWhiteSpace(projection) || width <= 0 || height <= 0)
                return BadRequest(new { message = "bbox, crs/srs, width and height are required." });

            var image = await _geoServerReadService.GetHeatmapAsync(userId, bbox, width, height, projection);
            return File(image.Bytes, image.ContentType);
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(WmsHeatmap));
        }
    }

    // GET /api/geometry/query -> a flat, paged list of the caller's shapes for the query panel.
    // Filtering (name contains, type), sorting and paging all run in SQL (WHERE / ORDER BY /
    // LIMIT-OFFSET over a UNION ALL of the three tables) — nothing is trimmed client-side.
    // Route note: "query" is a literal segment, which ASP.NET Core ranks above the {type} parameter
    // of GET /api/geometry/{type}, so this can never be captured as type="query".
    [HttpGet("query")]
    public async Task<ActionResult<GeometryQueryResponse>> Query([FromQuery] GeometryQueryRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            var result = await _geometryService.QueryPageAsync(request, userId);
            if (result is null)
                return BadRequest(new
                {
                    message = "Invalid query: sortBy must be name|type|createdAt, sortDir asc|desc, " +
                              "types a comma list of point|line|polygon.",
                });

            return Ok(result);
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Query));
        }
    }

    // GET /api/geometry/{type} -> the caller's shapes of a single type.
    [HttpGet("{type}")]
    public async Task<ActionResult<IReadOnlyList<GeometryResponse>>> List(string type)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            return Ok(await _geometryService.ListAsync(type, userId));
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(List));
        }
    }

    // POST /api/geometry/{type} -> save a drawn shape (owner = caller). Creating each geometry type
    // requires its matching draw permission: add_point / add_line / add_polygon.
    [HttpPost("{type}")]
    public async Task<ActionResult<GeometryResponse>> Create(string type, GeometryCreateRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            var permissionName = GeometryWritePermissions.ForType(type);
            if (permissionName is null)
                return BadRequest(new { message = "Unknown geometry type or invalid WKT for this type." });

            if (!await _userAdminService.HasPermissionAsync(userId, permissionName))
                return Forbid();

            var result = await _geometryService.CreateAsync(type, request, userId);
            return result.Status switch
            {
                UpdateStatus.InvalidGeometry =>
                    BadRequest(new { message = "Unknown geometry type or invalid WKT for this type." }),
                // The `code` field lets the client tell this 403 apart from the plain permission 403
                // above (which it reports as "no permission for this shape type").
                UpdateStatus.OutsideAuthorizedArea => StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "The shape is outside your authorized area.", code = "outside_authorized_area" }),
                _ => Ok(result.Response),
            };
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Create));
        }
    }

    // PUT /api/geometry/{type}/{id} -> update the caller's own shape when they still hold the matching
    // geometry permission: its name/color and, optionally, its geometry (location). Returns the row.
    [HttpPut("{type}/{id:int}")]
    public async Task<ActionResult<GeometryResponse>> Update(string type, int id, GeometryUpdateRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            var permissionName = GeometryWritePermissions.ForType(type);
            if (permissionName is null)
                return BadRequest(new { message = "Unknown geometry type or invalid WKT for this type." });

            if (!await _userAdminService.HasPermissionAsync(userId, permissionName))
                return Forbid();

            var result = await _geometryService.UpdateAsync(type, id, request, userId);
            return result.Status switch
            {
                UpdateStatus.NotFound => NotFound(new { message = "Shape not found." }),
                UpdateStatus.InvalidGeometry =>
                    BadRequest(new { message = "Invalid WKT, or its geometry type doesn't match this shape." }),
                UpdateStatus.OutsideAuthorizedArea => StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "The shape is outside your authorized area.", code = "outside_authorized_area" }),
                _ => Ok(result.Response),
            };
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Update));
        }
    }

    // DELETE /api/geometry/{type}/{id} -> soft-delete the caller's own shape when they still hold the
    // matching geometry permission.
    [HttpDelete("{type}/{id:int}")]
    public async Task<ActionResult> Delete(string type, int id)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            var permissionName = GeometryWritePermissions.ForType(type);
            if (permissionName is null)
                return BadRequest(new { message = "Unknown geometry type." });

            if (!await _userAdminService.HasPermissionAsync(userId, permissionName))
                return Forbid();

            if (!await _geometryService.DeleteAsync(type, id, userId))
                return NotFound(new { message = "Shape not found." });

            return NoContent();
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Delete));
        }
    }

    // POST /api/geometry/analysis -> count how many of the caller's shapes intersect a temporary
    // polygon. The polygon is NOT persisted; this is a read-only spatial query for the analysis tool.
    [HttpPost("analysis")]
    public async Task<ActionResult<AnalysisResponse>> Analyze(AnalysisRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            var result = await _geometryService.AnalyzeAsync(request.Wkt, userId);
            if (result is null)
                return BadRequest(new { message = "Invalid WKT, or the shape is not a polygon." });

            return Ok(result);
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Analyze));
        }
    }
}
