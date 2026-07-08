using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.DTOs;
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
    private static readonly IReadOnlyDictionary<string, string> CreatePermissionsByType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["point"] = "add_point",
            ["line"] = "add_line",
            ["polygon"] = "add_polygon",
        };

    private readonly IGeometryService _geometryService;
    private readonly IUserAdminService _userAdminService;
    private readonly ILogger<GeometryController> _logger;

    public GeometryController(
        IGeometryService geometryService,
        IUserAdminService userAdminService,
        ILogger<GeometryController> logger)
    {
        _geometryService = geometryService;
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

    // GET /api/geometry -> every shape the caller owns, grouped by type (one-shot map load).
    [HttpGet]
    public async Task<ActionResult<AllGeometryResponse>> GetAll()
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            return Ok(await _geometryService.ListAllAsync(userId));
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(GetAll));
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
                    message = "Invalid query: sortBy must be name|createdAt, sortDir asc|desc, " +
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

            if (!CreatePermissionsByType.TryGetValue(type, out var permissionName))
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

    // PUT /api/geometry/{type}/{id} -> update the caller's own shape: its name/color and, optionally,
    // its geometry (location). Returns the updated row.
    [HttpPut("{type}/{id:int}")]
    public async Task<ActionResult<GeometryResponse>> Update(string type, int id, GeometryUpdateRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

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

    // DELETE /api/geometry/{type}/{id} -> soft-delete the caller's own shape.
    [HttpDelete("{type}/{id:int}")]
    public async Task<ActionResult> Delete(string type, int id)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

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
