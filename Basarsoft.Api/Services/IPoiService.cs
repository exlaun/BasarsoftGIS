using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

public interface IPoiService
{
    // Every live POI in the system — the catalogue is shared, so there is no per-user filter here.
    Task<IReadOnlyList<PoiResponse>> ListAsync();

    // Creates a POI owned by userId. Validates the WKT is a point, the category exists, and the
    // point falls inside the caller's authorized area (when one is assigned).
    Task<PoiWriteResult> CreateAsync(PoiCreateRequest request, int userId);

    // Soft-deletes a POI. Non-admins may only delete their own; admins may delete any. False when
    // the POI doesn't exist or the caller isn't allowed to remove it (both surface as 404).
    Task<bool> DeleteAsync(int id, int userId, bool isAdmin);
}
