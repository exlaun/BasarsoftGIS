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
        Assert.Equal("Hafız Mustafa 1864", first.Name);
        Assert.Equal("POINT (28.974 41.016)", first.Wkt);
        Assert.Equal(12, first.CategoryId);
        Assert.Equal("Bakery", first.CategoryName);
        Assert.Equal("Food & Drink > Bakery", first.CategoryPath);
        Assert.Equal("#f97316", first.CategoryColor);
        Assert.Equal("bakery", first.CategoryIconKey);
        Assert.Equal(new TimeOnly(7, 0), first.OpenTime);
        Assert.Equal(new TimeOnly(23, 0), first.CloseTime);
        Assert.Equal(13, first.UserId);
        Assert.Equal("istanbul_operator", first.CreatedBy);
        Assert.Equal(DateTimeKind.Utc, first.CreatedAt.Kind);

        var second = rows[1];
        Assert.Null(second.CategoryColor);
        Assert.Equal(PoiIconCatalog.DefaultIconKey, second.CategoryIconKey);
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
                "name": "Ankara Explorer Hub",
                "category_id": 21,
                "category_name": "Visitor Center",
                "category_path": "Culture & Tourism > Visitor Center",
                "category_color": null,
                "category_icon_key": "../not-an-asset",
                "open_time": "not-a-time",
                "close_time": "18:00:00",
                "user_id": 1,
                "created_by": "admin",
                "created_at": 1784192400000,
                "modified_date": "2026-07-16T10:00:00Z"
              }
            },
            {
              "type": "Feature",
              "geometry": { "type": "Point", "coordinates": [28.974, 41.016] },
              "properties": {
                "id": "1",
                "name": "Hafız Mustafa 1864",
                "category_id": "12",
                "category_name": "Bakery",
                "category_path": "Food & Drink > Bakery",
                "category_color": "#f97316",
                "category_icon_key": "bakery",
                "open_time": "07:00:00",
                "close_time": "23:00:00",
                "user_id": "13",
                "created_by": "istanbul_operator",
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
