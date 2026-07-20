using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

// The shared POI catalogue. Reading is open to every authenticated user (POIs are common reference
// data, unlike the per-user drawing tables) and flows API -> GeoServer WFS (vw_poi) -> PostGIS, like
// the geometry reads; creating requires the add_poi permission and stays on EF; deleting is
// creator-or-admin. Category admin lives in AdminPoiCategoriesController.
[ApiController]
[Authorize]
[Route("api/poi")]
public class PoiController : ControllerBase
{
    private const string AddPoiPermission = "add_poi";

    private readonly IPoiService _pois;
    private readonly IPoiCategoryService _categories;
    private readonly IGeoServerReadService _geoServerReadService;
    private readonly IUserAdminService _userAdminService;
    private readonly ILogger<PoiController> _logger;

    public PoiController(
        IPoiService pois,
        IPoiCategoryService categories,
        IGeoServerReadService geoServerReadService,
        IUserAdminService userAdminService,
        ILogger<PoiController> logger)
    {
        _pois = pois;
        _categories = categories;
        _geoServerReadService = geoServerReadService;
        _userAdminService = userAdminService;
        _logger = logger;
    }

    // The logged-in user's id from the JWT "sub" claim (same idiom as GeometryController).
    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in PoiController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }

    // GET /api/poi -> every POI in the system (no per-user filter — the catalogue is shared).
    // Served from GeoServer's vw_poi view; GeoServer down means a clean 500, same as /api/geometry.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PoiResponse>>> List(CancellationToken ct)
    {
        try { return Ok(await _geoServerReadService.GetPoisAsync(ct)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new EmptyResult(); }
        catch (Exception ex) { return ServerError(ex, nameof(List)); }
    }

    // GET /api/poi/categories -> the flat category list. Also readable by non-admins: operators need
    // it for the add-POI dropdown and viewers for the info panel. Writes stay admin-only.
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<PoiCategoryResponse>>> Categories()
    {
        try { return Ok(await _categories.ListAsync()); }
        catch (Exception ex) { return ServerError(ex, nameof(Categories)); }
    }

    // GET /api/poi/icons -> stable storage keys plus labels for the category-admin icon picker.
    [HttpGet("icons")]
    public ActionResult<IReadOnlyList<PoiIconResponse>> Icons() => Ok(PoiIconCatalog.All);

    // POST /api/poi -> add a POI at the drawn point (owner = caller). Requires add_poi.
    [HttpPost]
    public async Task<ActionResult<PoiResponse>> Create(PoiCreateRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            if (!await _userAdminService.HasPermissionAsync(userId, AddPoiPermission))
                return Forbid();

            var result = await _pois.CreateAsync(request, userId);
            return result.Status switch
            {
                PoiWriteStatus.InvalidGeometry =>
                    BadRequest(new { message = "WKT must be a single valid point." }),
                PoiWriteStatus.CategoryNotFound =>
                    BadRequest(new { message = "Category not found.", code = "category_not_found" }),
                // The `code` field lets the client tell this 403 apart from the permission 403 above.
                PoiWriteStatus.OutsideAuthorizedArea => StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "The POI is outside your authorized area.", code = "outside_authorized_area" }),
                _ => Ok(result.Response),
            };
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Create));
        }
    }

    // DELETE /api/poi/{id} -> soft-delete. Creators may remove their own POIs; holders of
    // manage_pois (not just any admin permission) may remove anyone's.
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            var canManagePois = await _userAdminService.HasPermissionAsync(
                userId, Data.SeedData.ManagePoisPermission);
            if (!await _pois.DeleteAsync(id, userId, canManagePois))
                return NotFound(new { message = "POI not found." });

            return NoContent();
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Delete));
        }
    }
}
