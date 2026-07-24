using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Basarsoft.Api.Services;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Xunit;

namespace Basarsoft.Api.Tests;

public class GeometryAnalysisTests
{
    private readonly WKTReader _reader = new();

    [Fact]
    public async Task Analyze_CountsPrivateDrawingsAndSharedPoiStopRouteFeatures()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Points.AddRange(
            new PointFeature { UserId = 7, Geom = Read("POINT(1 1)") },
            new PointFeature { UserId = 8, Geom = Read("POINT(1 1)") });
        db.Lines.Add(new LineFeature
        {
            UserId = 7,
            Geom = Read("LINESTRING(-1 1, 3 1)"),
        });
        db.Polygons.Add(new PolygonFeature
        {
            UserId = 7,
            Geom = Read("POLYGON((0.5 0.5, 1.5 0.5, 1.5 1.5, 0.5 1.5, 0.5 0.5))"),
        });
        db.Pois.Add(new Poi { UserId = 8, CategoryId = 1, Geom = Read("POINT(1 1)") });
        db.Stops.Add(new Stop
        {
            UserId = 8,
            RouteId = 1,
            SequenceOrder = 1,
            Geom = Read("POINT(1 1)"),
        });
        db.Routes.AddRange(
            new TransportRoute
            {
                UserId = 8,
                Name = "Crossing",
                Geometry = (LineString)Read("LINESTRING(-1 1, 3 1)"),
            },
            new TransportRoute
            {
                UserId = 8,
                Name = "Unbuilt",
                Geometry = null,
            });
        await db.SaveChangesAsync();

        var service = new GeometryService(db, new UnrestrictedGeoAuthorizationService());
        var result = await service.AnalyzeAsync(
            "POLYGON((0 0, 2 0, 2 2, 0 2, 0 0))",
            userId: 7);

        Assert.NotNull(result);
        Assert.Equal(1, result.Points);
        Assert.Equal(1, result.Lines);
        Assert.Equal(1, result.Polygons);
        Assert.Equal(1, result.Pois);
        Assert.Equal(1, result.Stops);
        Assert.Equal(1, result.Routes);
        Assert.Equal(6, result.Total);
    }

    private Geometry Read(string wkt)
    {
        var geometry = _reader.Read(wkt);
        geometry.SRID = 4326;
        return geometry;
    }

    private sealed class UnrestrictedGeoAuthorizationService : IGeoAuthorizationService
    {
        public Task<Geometry?> GetEffectiveAreaAsync(int userId) => Task.FromResult<Geometry?>(null);
        public Task<GeoAreaResponse?> GetForUserAsync(int userId) => throw new NotSupportedException();
        public Task<GeoAreaResponse?> GetForRoleAsync(int roleId) => throw new NotSupportedException();
        public Task<GeoAreaWriteStatus> SetForUserAsync(int userId, string wkt) => throw new NotSupportedException();
        public Task<GeoAreaWriteStatus> SetForRoleAsync(int roleId, string wkt) => throw new NotSupportedException();
        public Task<bool> ClearForUserAsync(int userId) => throw new NotSupportedException();
        public Task<bool> ClearForRoleAsync(int roleId) => throw new NotSupportedException();
    }
}
