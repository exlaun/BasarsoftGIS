using System.Text.Json;
using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Services;

// Small, deterministic parser kept separate from HTTP orchestration so malformed/edge-case OSRM
// payloads and GeoJSON conversion can be covered without a live routing server.
public static class OsrmRouteParser
{
    private const int Srid = 4326;

    public static RoutingResult Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var codeElement)
                ? codeElement.GetString()
                : null;

            if (!string.Equals(code, "Ok", StringComparison.Ordinal))
                return MapErrorCode(code);

            if (!root.TryGetProperty("routes", out var routes) ||
                routes.ValueKind != JsonValueKind.Array ||
                routes.GetArrayLength() == 0)
            {
                return RoutingResult.NoRoute;
            }

            var route = routes[0];
            if (!route.TryGetProperty("distance", out var distanceElement) ||
                !route.TryGetProperty("duration", out var durationElement) ||
                !distanceElement.TryGetDouble(out var distance) ||
                !durationElement.TryGetDouble(out var duration) ||
                !double.IsFinite(distance) || distance < 0 ||
                !double.IsFinite(duration) || duration < 0 ||
                !route.TryGetProperty("geometry", out var geometry) ||
                !geometry.TryGetProperty("type", out var typeElement) ||
                !string.Equals(typeElement.GetString(), "LineString", StringComparison.Ordinal) ||
                !geometry.TryGetProperty("coordinates", out var coordinateArray) ||
                coordinateArray.ValueKind != JsonValueKind.Array)
            {
                return RoutingResult.Unavailable;
            }

            var coordinates = new List<Coordinate>();
            foreach (var pair in coordinateArray.EnumerateArray())
            {
                if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2 ||
                    !pair[0].TryGetDouble(out var longitude) ||
                    !pair[1].TryGetDouble(out var latitude) ||
                    !IsValidCoordinate(longitude, latitude))
                {
                    return RoutingResult.Unavailable;
                }

                coordinates.Add(new Coordinate(longitude, latitude));
            }

            if (coordinates.Count < 2)
                return RoutingResult.Unavailable;

            var line = new LineString(coordinates.ToArray()) { SRID = Srid };
            return RoutingResult.Ok(line, distance, duration);
        }
        catch (JsonException)
        {
            return RoutingResult.Unavailable;
        }
        catch (InvalidOperationException)
        {
            return RoutingResult.Unavailable;
        }
    }

    public static RoutingResult MapErrorCode(string? code) => code switch
    {
        "NoRoute" => RoutingResult.NoRoute,
        "InvalidUrl" or "InvalidOptions" or "InvalidQuery" or "InvalidValue" or
            "NoSegment" or "TooBig" => RoutingResult.InvalidCoordinates,
        _ => RoutingResult.Unavailable,
    };

    public static bool IsValidCoordinate(double longitude, double latitude) =>
        double.IsFinite(longitude) && double.IsFinite(latitude) &&
        longitude is >= -180 and <= 180 && latitude is >= -90 and <= 90;
}
