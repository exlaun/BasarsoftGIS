using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Security;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basarsoft.Api.Controllers;

// Admin CRUD for the POI category tree. Reading the list is NOT here — it lives on the open
// GET /api/poi/categories, because operators and viewers need it too. Unlike the RBAC admin
// controllers this one reads the JWT sub: tbl_poi_category carries creator/modifier user columns.
[ApiController]
[Authorize(Policy = PermissionRequirement.ManagePois)]
[Route("api/admin/poi-categories")]
public class AdminPoiCategoriesController : ControllerBase
{
    private readonly IPoiCategoryService _categories;
    private readonly ILogger<AdminPoiCategoriesController> _logger;

    public AdminPoiCategoriesController(
        IPoiCategoryService categories,
        ILogger<AdminPoiCategoriesController> logger)
    {
        _categories = categories;
        _logger = logger;
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    private ObjectResult ServerError(Exception ex, string action)
    {
        _logger.LogError(ex, "Unexpected error in AdminPoiCategoriesController.{Action}", action);
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { message = "An unexpected error occurred." });
    }

    private ActionResult<PoiCategoryResponse> MapWriteResult(PoiCategoryWriteResult result) =>
        result.Status switch
        {
            PoiCategoryWriteStatus.NotFound => NotFound(new { message = "Category not found." }),
            PoiCategoryWriteStatus.Conflict =>
                Conflict(new { message = "A category with that name already exists under the same parent." }),
            PoiCategoryWriteStatus.InvalidParent =>
                BadRequest(new { message = "Parent category not found, or it would create a cycle." }),
            PoiCategoryWriteStatus.InvalidIcon =>
                BadRequest(new { message = "Unknown POI icon key.", code = "invalid_icon_key" }),
            _ => Ok(result.Response),
        };

    [HttpPost]
    public async Task<ActionResult<PoiCategoryResponse>> Create(PoiCategorySaveRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            return MapWriteResult(await _categories.CreateAsync(request, userId));
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Create));
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PoiCategoryResponse>> Update(int id, PoiCategorySaveRequest request)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            return MapWriteResult(await _categories.UpdateAsync(id, request, userId));
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Update));
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        try
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized();

            return await _categories.DeleteAsync(id, userId) switch
            {
                PoiCategoryWriteStatus.NotFound => NotFound(new { message = "Category not found." }),
                PoiCategoryWriteStatus.InUse => Conflict(new
                {
                    message = "The category still has subcategories or POIs. Move or delete them first.",
                }),
                _ => NoContent(),
            };
        }
        catch (Exception ex)
        {
            return ServerError(ex, nameof(Delete));
        }
    }
}
