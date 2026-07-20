using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

// A rendered map image plus its content type, returned by the WMS proxy below.
public record GeoServerImage(byte[] Bytes, string ContentType);

// Reads the drawn geometry back through GeoServer instead of straight from EF Core, so the map is
// served by GeoServer. WFS (GetAllForUserAsync) feeds the editable vector layers; WMS (GetMapAsync)
// feeds the display layer. Only reads move here; creates/updates/deletes, the query panel and the
// analysis tool still run through GeometryService against PostGIS directly.
// Every method takes the request's CancellationToken: map panning abandons viewport renders fast,
// and without the token both this API and GeoServer would fully compute every stale frame.
public interface IGeoServerReadService
{
    // Every shape the given user owns, grouped by type, fetched from the per-user GeoServer SQL views.
    // `userId` fills each view's %uid% filter and is taken from the caller's JWT (never the request body),
    // so per-user isolation is enforced server-side and can't be spoofed by the client.
    Task<AllGeometryResponse> GetAllForUserAsync(int userId, CancellationToken ct = default);

    // Renders the given map viewport as a single PNG via GeoServer's WMS GetMap (all three per-user
    // views in one image). `userId` fills the views' %uid% filter (from the JWT). The client only steers
    // the viewport (bbox/size/crs); the layers are fixed server-side.
    Task<GeoServerImage> GetMapAsync(
        int userId, string bbox, int width, int height, string crs, CancellationToken ct = default);

    // Renders the user's shape density as a heat map PNG: same WMS GetMap contract as GetMapAsync but
    // against the vw_heat SQL view (all shapes collapsed to points), whose default GeoServer style is
    // the vec:Heatmap rendering transformation.
    Task<GeoServerImage> GetHeatmapAsync(
        int userId, string bbox, int width, int height, string crs, CancellationToken ct = default);

    // The whole POI catalogue from the vw_poi SQL view. No user id: POIs are shared reference data,
    // so the view takes no %uid% parameter. The view pre-computes the category breadcrumb and the
    // effective (inherited) category color, so the response matches what PoiService.CreateAsync emits.
    Task<IReadOnlyList<PoiResponse>> GetPoisAsync(CancellationToken ct = default);

    // Renders one location-analysis run as a weighted heat map PNG: same WMS GetMap contract as the
    // other map methods but against the vw_konum SQL view, whose %aid% parameter selects the stored
    // run (region clip + per-criterion weights). Ownership is checked by the caller BEFORE this —
    // the id is trusted here the same way userId is in the methods above.
    Task<GeoServerImage> GetLocationHeatmapAsync(
        int analysisId, string bbox, int width, int height, string crs, CancellationToken ct = default);
}
