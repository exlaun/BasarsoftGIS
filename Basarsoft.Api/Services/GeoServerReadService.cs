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

    public async Task<GeoServerImage> GetMapAsync(int userId, string bbox, int width, int height, string crs)
    {
        using var response = await _httpClient.GetAsync(BuildWmsUrl(bbox, width, height, crs, userId));
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
        // On a bad request GeoServer replies 200 with a service-exception XML, not an image. Treat that
        // as a failure so the controller returns 500 instead of shipping an error picture to the map.
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"GeoServer WMS returned '{contentType}', not an image.");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        return new GeoServerImage(bytes, contentType);
    }

    private string BuildWmsUrl(string bbox, int width, int height, string crs, int userId)
    {
        var endpoint = $"{_settings.BaseUrl.TrimEnd('/')}/{_settings.Workspace}/wms";
        var layers = string.Join(',', Layers.Select(l => $"{_settings.Workspace}:{l.Layer}"));
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
            // One group applies to all three layers (GeoServer-verified). uid comes from the JWT.
            ["viewparams"] = $"uid:{userId}",
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
