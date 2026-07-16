using System.Net;
using System.Text;
using Basarsoft.Api.Services;
using Basarsoft.Api.Settings;
using Xunit;

namespace Basarsoft.Api.Tests;

public class GeoServerReadServiceTests
{
    [Fact]
    public async Task GetPoisAsync_UsesSharedWfsLayerWithoutUserViewParams()
    {
        var handler = new StubHttpMessageHandler(PoiFeatureCollection);
        var service = CreateService(handler);

        await service.GetPoisAsync();

        var requestUri = Assert.IsType<Uri>(handler.RequestUri);
        Assert.Equal("/geoserver/basarsoft/ows", requestUri.AbsolutePath);
        Assert.Contains("typeNames=basarsoft%3Avw_poi", requestUri.Query);
        Assert.Contains("srsName=EPSG%3A4326", requestUri.Query);
        Assert.False(requestUri.Query.Contains("viewparams", StringComparison.OrdinalIgnoreCase));
        Assert.False(requestUri.Query.Contains("uid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetPoisAsync_ParsesSortsAndPreservesPoiContract()
    {
        var service = CreateService(new StubHttpMessageHandler(PoiFeatureCollection));

        var rows = await service.GetPoisAsync();

        Assert.Equal(2, rows.Count); // the feature with null geometry is deliberately skipped
        Assert.Equal([1, 2], rows.Select(row => row.Id));

        var first = rows[0];
        Assert.Equal("Flagship Store — İstanbul Nişantaşı", first.Name);
        Assert.Equal("POINT (28.995 41.048)", first.Wkt);
        Assert.Equal(3, first.CategoryId);
        Assert.Equal("Flagship Store", first.CategoryName);
        Assert.Equal("Customer Service > Retail > Flagship Store", first.CategoryPath);
        Assert.Equal("#7c3aed", first.CategoryColor);
        Assert.Equal(new TimeOnly(9, 30), first.OpenTime);
        Assert.Equal(new TimeOnly(18, 45), first.CloseTime);
        Assert.Equal(6, first.UserId);
        Assert.Equal("viewer", first.CreatedBy);
        Assert.Equal(DateTimeKind.Utc, first.CreatedAt.Kind);

        var second = rows[1];
        Assert.Null(second.CategoryColor);
        Assert.Equal(default, second.OpenTime); // malformed WFS time degrades without losing the row
    }

    private static GeoServerReadService CreateService(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler), new GeoServerSettings
        {
            BaseUrl = "http://localhost:8080/geoserver",
            Workspace = "basarsoft",
        });

    private sealed class StubHttpMessageHandler(string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private const string PoiFeatureCollection = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "geometry": { "type": "Point", "coordinates": [32.854, 39.92] },
              "properties": {
                "id": 2,
                "name": "Fallback POI",
                "category_id": 11,
                "category_name": "Regional Warehouse",
                "category_path": "Operations > Depot > Regional Warehouse",
                "category_color": null,
                "open_time": "not-a-time",
                "close_time": "17:00:00",
                "user_id": 1,
                "created_by": "admin",
                "created_at": 1784192400000,
                "modified_date": "2026-07-16T10:00:00Z"
              }
            },
            {
              "type": "Feature",
              "geometry": { "type": "Point", "coordinates": [28.995, 41.048] },
              "properties": {
                "id": "1",
                "name": "Flagship Store — İstanbul Nişantaşı",
                "category_id": "3",
                "category_name": "Flagship Store",
                "category_path": "Customer Service > Retail > Flagship Store",
                "category_color": "#7c3aed",
                "open_time": "09:30:00",
                "close_time": "18:45:00",
                "user_id": "6",
                "created_by": "viewer",
                "created_at": "2026-07-16T09:00:00Z",
                "modified_date": "2026-07-16T10:00:00Z"
              }
            },
            {
              "type": "Feature",
              "geometry": null,
              "properties": { "id": 99 }
            }
          ]
        }
        """;
}
