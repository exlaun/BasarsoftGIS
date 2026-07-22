using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

// POI writes only: listing moved to IGeoServerReadService.GetPoisAsync (API -> GeoServer WFS), the
// same split the geometry endpoints use.
public interface IPoiService
{
    // Creates a POI owned by userId. Validates the WKT is a point, the category exists, and the
    // point falls inside the caller's authorized area (when one is assigned).
    Task<PoiWriteResult> CreateAsync(PoiCreateRequest request, int userId);

    // Soft-deletes a POI after the controller's manage_pois gate. The ownership argument remains for
    // service-level compatibility, but the HTTP endpoint now always calls this as an administrator.
    // NotFound when it doesn't exist (or isn't theirs for a non-admin call); OutsideAuthorizedArea
    // when the POI sits outside an area-restricted caller's boundary.
    Task<DeleteStatus> DeleteAsync(int id, int userId, bool isAdmin);
}
