using System.Globalization;
using System.Text.Json;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Settings;
using Microsoft.AspNetCore.WebUtilities;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;

namespace Basarsoft.Api.Services;

// Reads the drawn geometry back through GeoServer's WFS (as GeoJSON) and re-emits it in the same WKT
// shape the map already consumes, so moving the read source to GeoServer needs no frontend change.
public class GeoServerReadService : IGeoServerReadService
{
    private readonly HttpClient _httpClient;
    private readonly GeoServerSettings _settings;

    // GeoJSON4STJ converters, so System.Text.Json can turn a GeoJSON geometry into an NTS Geometry.
    private static readonly JsonSerializerOptions GeoJsonOptions = CreateGeoJsonOptions();

    // GeoServer publishes one per-user SQL view per geometry type; these are their layer names, paired
    // with the apiType the response groups them under. Order matches the AllGeometryResponse assembly.
    private static readonly (string ApiType, string Layer)[] Layers =
    {
        ("point", "vw_point"),
        ("line", "vw_line"),
        ("polygon", "vw_polygon"),
    };

    // SQL view that collapses every live shape to a point (centroids for lines/polygons); its default
    // GeoServer style is the vec:Heatmap rendering transformation, so a GetMap on it IS the heat map.
    private const string HeatLayer = "vw_heat";

    // SQL view over the shared POI catalogue (joined to its category tree for breadcrumb + inherited
    // color). Deliberately NOT in Layers: that array drives the per-user %uid% WFS loop, while vw_poi
    // takes no parameters. Its default GeoServer style colors markers by category and labels on zoom.
    private const string PoiLayer = "vw_poi";

    // SQL view behind the location-analysis heat map: POIs inside the stored run's region, each
    // carrying its matching criterion's weight. Parameterized by %aid% (the run id) alone; its default
    // style is the weighted vec:Heatmap rendering transformation (weightAttr = weight).
    private const string KonumLayer = "vw_konum";

    public GeoServerReadService(HttpClient httpClient, GeoServerSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task<AllGeometryResponse> GetAllForUserAsync(int userId)
    {
        // HttpClient is thread-safe, so the three layer fetches run concurrently (unlike EF's DbContext).
        var results = await Task.WhenAll(Layers.Select(l => FetchLayerAsync(l.Layer, userId)));

        return new AllGeometryResponse
        {
            Points = results[0],
            Lines = results[1],
            Polygons = results[2],
        };
    }

    public Task<GeoServerImage> GetMapAsync(int userId, string bbox, int width, int height, string crs)
    {
        // POIs go last so their markers/labels draw on top of the shapes. The shared viewparams group
        // is harmless to vw_poi: GeoServer ignores parameters a virtual table doesn't declare.
        var layers = string.Join(',', Layers
            .Select(l => $"{_settings.Workspace}:{l.Layer}")
            .Append($"{_settings.Workspace}:{PoiLayer}"));
        return FetchMapAsync(BuildWmsUrl(layers, bbox, width, height, crs, $"uid:{userId}"));
    }

    public async Task<IReadOnlyList<PoiResponse>> GetPoisAsync()
    {
        var json = await _httpClient.GetStringAsync(BuildPoiWfsUrl());
        return ParsePoiFeatureCollection(json);
    }

    public Task<GeoServerImage> GetHeatmapAsync(int userId, string bbox, int width, int height, string crs) =>
        FetchMapAsync(BuildWmsUrl($"{_settings.Workspace}:{HeatLayer}", bbox, width, height, crs, $"uid:{userId}"));

    public Task<GeoServerImage> GetLocationHeatmapAsync(int analysisId, string bbox, int width, int height, string crs) =>
        FetchMapAsync(BuildWmsUrl($"{_settings.Workspace}:{KonumLayer}", bbox, width, height, crs, $"aid:{analysisId}"));

    private async Task<GeoServerImage> FetchMapAsync(string url)
    {
        using var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
        // On a bad request GeoServer replies 200 with a service-exception XML, not an image. Treat that
        // as a failure so the controller returns 500 instead of shipping an error picture to the map.
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"GeoServer WMS returned '{contentType}', not an image.");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        return new GeoServerImage(bytes, contentType);
    }

    private string BuildWmsUrl(string layers, string bbox, int width, int height, string crs, string viewparams)
    {
        var endpoint = $"{_settings.BaseUrl.TrimEnd('/')}/{_settings.Workspace}/wms";
        var query = new Dictionary<string, string?>
        {
            ["service"] = "WMS",
            ["version"] = "1.1.1",
            ["request"] = "GetMap",
            ["layers"] = layers,
            ["styles"] = "",
            ["format"] = "image/png",
            ["transparent"] = "true",
            // The client steers the viewport; SRS is whatever the OpenLayers view uses (EPSG:3857).
            ["srs"] = crs,
            ["bbox"] = bbox,
            ["width"] = width.ToString(CultureInfo.InvariantCulture),
            ["height"] = height.ToString(CultureInfo.InvariantCulture),
            // One group applies to every requested layer (GeoServer-verified); layers without the
            // parameter ignore it. The value is always server-built ("uid:<jwt id>" / "aid:<run id>").
            ["viewparams"] = viewparams,
        };
        return QueryHelpers.AddQueryString(endpoint, query);
    }

    private async Task<IReadOnlyList<GeometryResponse>> FetchLayerAsync(string layer, int userId)
    {
        var json = await _httpClient.GetStringAsync(BuildWfsUrl(layer, userId));
        return ParseFeatureCollection(json);
    }

    private string BuildWfsUrl(string layer, int userId)
    {
        var endpoint = $"{_settings.BaseUrl.TrimEnd('/')}/{_settings.Workspace}/ows";
        var query = new Dictionary<string, string?>
        {
            ["service"] = "WFS",
            ["version"] = "2.0.0",
            ["request"] = "GetFeature",
            ["typeNames"] = $"{_settings.Workspace}:{layer}",
            ["outputFormat"] = "application/json",
            // WGS84 lon/lat GeoJSON — AsText() then yields the same WKT the old EF path produced, which
            // the OpenLayers client reprojects 4326 -> 3857 exactly as before.
            ["srsName"] = "EPSG:4326",
            // Fills the SQL view's %uid% placeholder. Trusted: it comes from the caller's JWT.
            ["viewparams"] = $"uid:{userId}",
        };
        return QueryHelpers.AddQueryString(endpoint, query);
    }

    private string BuildPoiWfsUrl()
    {
        var endpoint = $"{_settings.BaseUrl.TrimEnd('/')}/{_settings.Workspace}/ows";
        var query = new Dictionary<string, string?>
        {
            ["service"] = "WFS",
            ["version"] = "2.0.0",
            ["request"] = "GetFeature",
            ["typeNames"] = $"{_settings.Workspace}:{PoiLayer}",
            ["outputFormat"] = "application/json",
            ["srsName"] = "EPSG:4326",
            // No viewparams: vw_poi has no %uid% placeholder — the POI catalogue is shared.
        };
        return QueryHelpers.AddQueryString(endpoint, query);
    }

    // vw_poi rows -> the same PoiResponse the old EF read produced. The view already resolved the
    // category breadcrumb, the inherited color and the creator's username in SQL, and casts the two
    // time columns to 'HH24:MI:SS' text, so this parse stays flat.
    private static IReadOnlyList<PoiResponse> ParsePoiFeatureCollection(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<PoiResponse>();
        }

        var rows = new List<PoiResponse>(features.GetArrayLength());
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geomEl) ||
                geomEl.ValueKind == JsonValueKind.Null)
            {
                continue; // a row with no geometry can't be drawn
            }

            var geometry = JsonSerializer.Deserialize<Geometry>(geomEl.GetRawText(), GeoJsonOptions);
            if (geometry is null || geometry.IsEmpty)
                continue;

            var props = feature.GetProperty("properties");
            rows.Add(new PoiResponse
            {
                Id = ReadInt(props, "id") ?? 0,
                Wkt = geometry.AsText(),
                Name = ReadString(props, "name") ?? string.Empty,
                CategoryId = ReadInt(props, "category_id") ?? 0,
                CategoryName = ReadString(props, "category_name") ?? string.Empty,
                CategoryPath = ReadString(props, "category_path") ?? string.Empty,
                CategoryColor = ReadString(props, "category_color"),
                CategoryIconKey = PoiIconCatalog.NormalizeOrDefault(
                    ReadString(props, "category_icon_key")),
                OpenTime = ReadTime(props, "open_time"),
                CloseTime = ReadTime(props, "close_time"),
                UserId = ReadInt(props, "user_id") ?? 0,
                CreatedBy = ReadString(props, "created_by") ?? string.Empty,
                CreatedAt = ReadDate(props, "created_at"),
                ModifiedDate = ReadDate(props, "modified_date"),
            });
        }

        // WFS gives no ordering guarantee; the old EF read ordered by id, so keep that contract.
        rows.Sort((a, b) => a.Id.CompareTo(b.Id));
        return rows;
    }

    // Turn a GeoServer GeoJSON FeatureCollection into the map's WKT response rows. Property values are
    // read explicitly by the SQL-view column names so their CLR types stay under our control; only the
    // geometry goes through the NTS GeoJSON reader (then AsText() gives WKT).
    private static IReadOnlyList<GeometryResponse> ParseFeatureCollection(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<GeometryResponse>();
        }

        var rows = new List<GeometryResponse>(features.GetArrayLength());
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geomEl) ||
                geomEl.ValueKind == JsonValueKind.Null)
            {
                continue; // a row with no geometry can't be drawn
            }

            var geometry = JsonSerializer.Deserialize<Geometry>(geomEl.GetRawText(), GeoJsonOptions);
            if (geometry is null || geometry.IsEmpty)
                continue;

            var props = feature.GetProperty("properties");
            rows.Add(new GeometryResponse
            {
                Id = ReadInt(props, "id") ?? 0,
                Wkt = geometry.AsText(),
                Name = ReadString(props, "name"),
                Color = ReadString(props, "color"),
                CreatedAt = ReadDate(props, "created_at"),
                ModifiedDate = ReadDate(props, "modified_date"),
                ModifiedUserId = ReadInt(props, "modified_user_id"),
            });
        }

        return rows;
    }

    private static JsonSerializerOptions CreateGeoJsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new GeoJsonConverterFactory());
        return options;
    }

    private static string? ReadString(JsonElement props, string name) =>
        props.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static int? ReadInt(JsonElement props, string name)
    {
        if (!props.TryGetProperty(name, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n,
            _ => null,
        };
    }

    // The view emits times as 'HH24:MI:SS' text (to_char), so this is a plain string parse; a missing
    // or malformed value degrades to 00:00:00 instead of failing the whole catalogue.
    private static TimeOnly ReadTime(JsonElement props, string name) =>
        props.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String &&
        TimeOnly.TryParse(el.GetString(), CultureInfo.InvariantCulture, out var time)
            ? time
            : default;

    // GeoServer usually encodes timestamps as ISO-8601 strings in GeoJSON, but tolerate epoch millis too.
    private static DateTime ReadDate(JsonElement props, string name)
    {
        if (!props.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return default;
        if (el.ValueKind == JsonValueKind.String)
        {
            if (el.TryGetDateTime(out var dt))
                return dt;
            if (DateTime.TryParse(el.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal, out var parsed))
                return parsed;
        }
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        return default;
    }
}
