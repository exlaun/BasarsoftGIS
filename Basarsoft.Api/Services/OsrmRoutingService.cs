using System.Globalization;
using Basarsoft.Api.Settings;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;

namespace Basarsoft.Api.Services;

public class OsrmRoutingService : IRoutingService
{
    private readonly HttpClient _httpClient;
    private readonly RoutingSettings _settings;
    private readonly ILogger<OsrmRoutingService> _logger;

    public OsrmRoutingService(
        HttpClient httpClient,
        IOptions<RoutingSettings> settings,
        ILogger<OsrmRoutingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<RoutingResult> BuildRouteAsync(
        IReadOnlyList<Coordinate> orderedCoordinates,
        CancellationToken cancellationToken = default)
    {
        if (orderedCoordinates.Count < 2 || orderedCoordinates.Any(
                coordinate => !OsrmRouteParser.IsValidCoordinate(coordinate.X, coordinate.Y)))
        {
            return RoutingResult.InvalidCoordinates;
        }

        var primary = await TryEndpointAsync(
            _settings.PrimaryBaseUrl, orderedCoordinates, cancellationToken);
        if (!primary.IsTransientFailure)
            return primary.Result;

        var fallback = _settings.FallbackBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(fallback) ||
            string.Equals(
                NormalizeBaseUrl(fallback),
                NormalizeBaseUrl(_settings.PrimaryBaseUrl),
                StringComparison.OrdinalIgnoreCase))
        {
            return RoutingResult.Unavailable;
        }

        return (await TryEndpointAsync(fallback, orderedCoordinates, cancellationToken)).Result;
    }

    private async Task<RoutingAttempt> TryEndpointAsync(
        string baseUrl,
        IReadOnlyList<Coordinate> coordinates,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(BuildUrl(baseUrl, coordinates), UriKind.Absolute, out var uri))
            return RoutingAttempt.Terminal(RoutingResult.Unavailable);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_settings.TimeoutSeconds, 1, 300)));

        try
        {
            using var response = await _httpClient.GetAsync(uri, timeout.Token);
            var json = await response.Content.ReadAsStringAsync(timeout.Token);

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogWarning("OSRM endpoint {Endpoint} returned {StatusCode}",
                    uri.GetLeftPart(UriPartial.Authority), (int)response.StatusCode);
                return RoutingAttempt.Transient();
            }

            return RoutingAttempt.Terminal(OsrmRouteParser.Parse(json));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("OSRM endpoint {Endpoint} timed out", uri.GetLeftPart(UriPartial.Authority));
            return RoutingAttempt.Transient();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not connect to OSRM endpoint {Endpoint}",
                uri.GetLeftPart(UriPartial.Authority));
            return RoutingAttempt.Transient();
        }
    }

    private string BuildUrl(string baseUrl, IReadOnlyList<Coordinate> coordinates)
    {
        var coordinateText = string.Join(';', coordinates.Select(coordinate =>
            $"{coordinate.X.ToString("R", CultureInfo.InvariantCulture)},{coordinate.Y.ToString("R", CultureInfo.InvariantCulture)}"));
        return $"{NormalizeBaseUrl(baseUrl)}/route/v1/{Uri.EscapeDataString(_settings.Profile)}/{coordinateText}" +
               "?overview=full&geometries=geojson&steps=false";
    }

    private static string NormalizeBaseUrl(string baseUrl) => baseUrl.Trim().TrimEnd('/');

    private sealed record RoutingAttempt(RoutingResult Result, bool IsTransientFailure)
    {
        public static RoutingAttempt Terminal(RoutingResult result) => new(result, false);
        public static RoutingAttempt Transient() => new(RoutingResult.Unavailable, true);
    }
}
