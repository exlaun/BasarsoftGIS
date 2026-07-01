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
    private readonly IGeometryService _geometryService;

    public GeometryController(IGeometryService geometryService)
    {
        _geometryService = geometryService;
    }

    // The logged-in user's id, taken from the JWT "sub" claim (same claim AuthController.Me reads).
    private int CurrentUserId => int.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    // GET /api/geometry -> every shape the caller owns, grouped by type (one-shot map load).
    [HttpGet]
    public async Task<ActionResult<AllGeometryResponse>> GetAll()
        => Ok(await _geometryService.ListAllAsync(CurrentUserId));

    // GET /api/geometry/{type} -> the caller's shapes of a single type.
    [HttpGet("{type}")]
    public async Task<ActionResult<IReadOnlyList<GeometryResponse>>> List(string type)
        => Ok(await _geometryService.ListAsync(type, CurrentUserId));

    // POST /api/geometry/{type} -> save a drawn shape (owner = caller).
    [HttpPost("{type}")]
    public async Task<ActionResult<GeometryResponse>> Create(string type, GeometryCreateRequest request)
    {
        var result = await _geometryService.CreateAsync(type, request, CurrentUserId);
        if (result is null)
            return BadRequest(new { message = "Unknown geometry type or invalid WKT for this type." });

        return Ok(result);
    }

    // DELETE /api/geometry/{type}/{id} -> soft-delete the caller's own shape.
    [HttpDelete("{type}/{id:int}")]
    public async Task<ActionResult> Delete(string type, int id)
    {
        if (!await _geometryService.DeleteAsync(type, id, CurrentUserId))
            return NotFound(new { message = "Shape not found." });

        return NoContent();
    }
}
