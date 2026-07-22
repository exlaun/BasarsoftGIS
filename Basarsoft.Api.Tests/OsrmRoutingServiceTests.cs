using System.Net;
using Basarsoft.Api.Services;
using Basarsoft.Api.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Xunit;

namespace Basarsoft.Api.Tests;

public class OsrmRoutingServiceTests
{
    private const string SuccessJson = """
        {
          "code": "Ok",
          "routes": [{
            "distance": 1234.5,
            "duration": 321.5,
            "geometry": {"type":"LineString","coordinates":[[29.0,41.0],[29.1,41.1],[29.2,41.2]]}
          }]
        }
        """;

    [Fact]
    public void Parser_ConvertsGeoJsonToSrid4326LineString()
    {
        var result = OsrmRouteParser.Parse(SuccessJson);

        Assert.Equal(RoutingStatus.Success, result.Status);
        Assert.NotNull(result.Geometry);
        Assert.Equal(4326, result.Geometry!.SRID);
        Assert.Equal(3, result.Geometry.NumPoints);
        Assert.Equal(29.2, result.Geometry.GetCoordinateN(2).X);
        Assert.Equal(1234.5, result.DistanceMeters);
        Assert.Equal(321.5, result.DurationSeconds);
    }

    [Fact]
    public async Task LocalSuccess_UsesOrderedCoordinatesAndRequiredOptions()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK, SuccessJson));
        var service = NewService(handler);

        var result = await service.BuildRouteAsync(
            [new Coordinate(29, 41), new Coordinate(30, 40)]);

        Assert.Equal(RoutingStatus.Success, result.Status);
        var request = Assert.Single(handler.Requests).ToString();
        Assert.StartsWith("http://local:5000/route/v1/driving/29,41;30,40", request);
        Assert.Contains("overview=full", request);
        Assert.Contains("geometries=geojson", request);
        Assert.Contains("steps=false", request);
    }

    [Fact]
    public async Task TransientLocalFailure_UsesConfiguredFallback()
    {
        var handler = new QueueHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => Json(HttpStatusCode.OK, SuccessJson));
        var service = NewService(handler, fallback: "https://fallback.example");

        var result = await service.BuildRouteAsync(
            [new Coordinate(29, 41), new Coordinate(30, 40)]);

        Assert.Equal(RoutingStatus.Success, result.Status);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("local", handler.Requests[0].Host);
        Assert.Equal("fallback.example", handler.Requests[1].Host);
    }

    [Fact]
    public async Task DisabledFallback_ReportsUnavailableAfterConnectionFailure()
    {
        var handler = new QueueHandler(_ => throw new HttpRequestException("offline"));
        var result = await NewService(handler).BuildRouteAsync(
            [new Coordinate(29, 41), new Coordinate(30, 40)]);

        Assert.Equal(RoutingStatus.Unavailable, result.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task BothEndpointsUnavailable_ReportsUnavailable()
    {
        var handler = new QueueHandler(
            _ => throw new HttpRequestException("local offline"),
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var result = await NewService(handler, fallback: "https://fallback.example")
            .BuildRouteAsync([new Coordinate(29, 41), new Coordinate(30, 40)]);

        Assert.Equal(RoutingStatus.Unavailable, result.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task NoRoute_IsTerminalAndDoesNotUseFallback()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.BadRequest, "{\"code\":\"NoRoute\"}"));
        var result = await NewService(handler, fallback: "https://fallback.example")
            .BuildRouteAsync([new Coordinate(29, 41), new Coordinate(30, 40)]);

        Assert.Equal(RoutingStatus.NoRoute, result.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task InvalidCoordinate_IsRejectedBeforeHttp()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK, SuccessJson));
        var result = await NewService(handler).BuildRouteAsync(
            [new Coordinate(181, 41), new Coordinate(30, 40)]);

        Assert.Equal(RoutingStatus.InvalidCoordinates, result.Status);
        Assert.Empty(handler.Requests);
    }

    private static OsrmRoutingService NewService(QueueHandler handler, string? fallback = null) =>
        new(
            new HttpClient(handler),
            Options.Create(new RoutingSettings
            {
                PrimaryBaseUrl = "http://local:5000",
                FallbackBaseUrl = fallback,
                Profile = "driving",
                TimeoutSeconds = 10,
            }),
            NullLogger<OsrmRoutingService>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json),
    };

    private sealed class QueueHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private int _index;
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var response = responses[Math.Min(_index++, responses.Length - 1)](request);
            return Task.FromResult(response);
        }
    }
}
