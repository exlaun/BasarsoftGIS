using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

// The "Konum Analizi" (location analysis) run store. Creating a run validates the mentor's rules
// (one region source, 2..5 criteria, weights sum to exactly 100) and persists region + criteria so
// GeoServer's vw_konum view can be driven by nothing but the run's id (viewparams aid:<id>).
public interface ILocationAnalysisService
{
    // Validates and stores a run for the calling user. Every failure is a client mistake -> the
    // controller turns each status into a 400 with a `code`; Success carries the run id + echo data.
    Task<LocationAnalysisWriteResult> CreateAsync(LocationAnalysisCreateRequest request, int userId);

    // Whether the run exists, is live, and belongs to the caller. "Not yours" and "doesn't exist" are
    // deliberately the same answer (404 upstream) so run ids leak nothing — the PoiService.Delete idiom.
    Task<bool> IsOwnedAsync(int id, int userId);
}
