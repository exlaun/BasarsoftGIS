using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Basarsoft.Api.Controllers;

// Turkey's 81 provinces — static reference data for the location-analysis region dropdown. Readable
// by every authenticated user (the tool must work for the permission-free Viewer role too); there are
// no writes, the table is seeded once at startup by ProvinceSeeder. Thin enough to query the context
// directly, like the reference reads elsewhere.
[ApiController]
[Authorize]
[Route("api/provinces")]
public class ProvinceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ProvinceController> _logger;

    public ProvinceController(AppDbContext db, ILogger<ProvinceController> logger)
    {
        _db = db;
        _logger = logger;
    }

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
