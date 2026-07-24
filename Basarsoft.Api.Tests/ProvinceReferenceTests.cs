using Basarsoft.Api.Controllers;
using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using Xunit;

namespace Basarsoft.Api.Tests;

public class ProvinceReferenceTests
{
    private static readonly GeometryFactory GeometryFactory =
        new(new PrecisionModel(), 4326);

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task CommittedCatalog_HasExactCoveredCapitalMetadata()
    {
        var catalog = await new ProvinceCatalog().GetAsync();

        Assert.Equal(ProvinceCatalog.ExpectedProvinceCount, catalog.Count);
        Assert.Equal(catalog.Count, catalog.Select(entry => entry.Name).Distinct().Count());
        Assert.Equal(7, catalog.Select(entry => entry.Region).Distinct().Count());
        Assert.All(catalog, entry =>
        {
            Assert.True(entry.Boundary.IsValid);
            Assert.True(entry.Boundary.Covers(entry.CapitalGeom));
            Assert.Matches("^#[0-9a-fA-F]{6}$", entry.Color);
            Assert.False(string.IsNullOrWhiteSpace(entry.SourceKey));
            Assert.False(string.IsNullOrWhiteSpace(entry.SourceId));
            Assert.StartsWith("relation/", entry.BoundarySourceId);
            Assert.False(string.IsNullOrWhiteSpace(entry.GeometrySource));
        });

        Assert.Equal("İzmit", catalog.Single(entry => entry.Name == "Kocaeli").CapitalName);
        Assert.Equal("Adapazarı", catalog.Single(entry => entry.Name == "Sakarya").CapitalName);
        Assert.Equal("Antakya", catalog.Single(entry => entry.Name == "Hatay").CapitalName);
    }

    [Fact]
    public async Task Seeder_UpsertsExactCatalogAndPreservesExistingIds()
    {
        await using var db = NewDb();
        var entries = Entries();
        var catalog = new CatalogStub(entries);

        await ProvinceSeeder.SeedAsync(db, catalog);
        var firstId = await db.Provinces
            .Where(province => province.Name == "Province 01")
            .Select(province => province.Id)
            .SingleAsync();

        await ProvinceSeeder.SeedAsync(db, catalog);

        Assert.Equal(ProvinceCatalog.ExpectedProvinceCount, await db.Provinces.CountAsync());
        Assert.Equal(firstId, await db.Provinces
            .Where(province => province.Name == "Province 01")
            .Select(province => province.Id)
            .SingleAsync());
    }

    [Fact]
    public async Task Seeder_UpdatesExistingBoundaryWhilePreservingId()
    {
        await using var db = NewDb();
        var entries = Entries();
        await ProvinceSeeder.SeedAsync(db, new CatalogStub(entries));

        var existing = await db.Provinces.SingleAsync(province => province.Name == "Province 01");
        var existingId = existing.Id;
        var updatedBoundary = CreateBoundary(42);
        var updatedEntries = entries
            .Select(entry => entry.Name == existing.Name
                ? entry with { Boundary = updatedBoundary }
                : entry)
            .ToArray();

        await ProvinceSeeder.SeedAsync(db, new CatalogStub(updatedEntries));

        var updated = await db.Provinces.SingleAsync(province => province.Name == existing.Name);
        Assert.Equal(existingId, updated.Id);
        Assert.True(updated.Geom.EqualsExact(updatedBoundary));
    }

    [Fact]
    public async Task Seeder_FailsFastWhenPersistedProvinceIsNotInCatalog()
    {
        await using var db = NewDb();
        db.Provinces.Add(new Province
        {
            Name = "Unexpected province",
            Geom = CreateBoundary(42),
        });
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ProvinceSeeder.SeedAsync(db, new CatalogStub(Entries())));

        Assert.Contains("Unexpected province", exception.Message);
        Assert.Equal(1, await db.Provinces.CountAsync());
    }

    [Fact]
    public async Task Map_ReturnsMatchingBoundaryAndCapitalPresentation()
    {
        await using var db = NewDb();
        var entries = Entries();
        var catalog = new CatalogStub(entries);
        await ProvinceSeeder.SeedAsync(db, catalog);
        var controller = new ProvinceController(
            db, catalog, new MemoryCache(new MemoryCacheOptions()), NullLogger<ProvinceController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var action = await controller.Map(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<ProvinceMapResponse>>(ok.Value);
        Assert.Equal(ProvinceCatalog.ExpectedProvinceCount, response.Count);
        var first = response.Single(row => row.Name == "Province 01");
        var source = entries.Single(entry => entry.Name == first.Name);
        Assert.Equal(source.Region, first.Region);
        Assert.Equal(source.Color, first.Color);
        Assert.Equal(source.CapitalName, first.CapitalName);
        Assert.Equal(source.Boundary.AsText(), first.BoundaryWkt);
        Assert.Equal(source.CapitalGeom.AsText(), first.CapitalWkt);
        Assert.Equal("private, no-cache", controller.Response.Headers.CacheControl);
        Assert.Matches("^\"[0-9A-F]{64}\"$", controller.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task Map_CachesTheMaterializedReferenceDtos()
    {
        await using var db = NewDb();
        var catalog = new CatalogStub(Entries());
        await ProvinceSeeder.SeedAsync(db, catalog);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new ProvinceController(
            db, catalog, cache, NullLogger<ProvinceController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var callsBeforeMap = catalog.Calls;

        var first = await controller.Map(CancellationToken.None);
        var firstResponse = Assert.IsAssignableFrom<IReadOnlyList<ProvinceMapResponse>>(
            Assert.IsType<OkObjectResult>(first.Result).Value);
        var firstBoundary = firstResponse.Single(row => row.Name == "Province 01").BoundaryWkt;

        var persisted = await db.Provinces.SingleAsync(province => province.Name == "Province 01");
        persisted.Geom = CreateBoundary(42);
        await db.SaveChangesAsync();

        var second = await controller.Map(CancellationToken.None);
        var secondResponse = Assert.IsAssignableFrom<IReadOnlyList<ProvinceMapResponse>>(
            Assert.IsType<OkObjectResult>(second.Result).Value);

        Assert.Equal(callsBeforeMap + 1, catalog.Calls);
        Assert.Equal(firstBoundary, secondResponse.Single(row => row.Name == "Province 01").BoundaryWkt);
    }

    [Fact]
    public async Task Map_ReturnsNotModifiedForMatchingEntityTag()
    {
        await using var db = NewDb();
        var catalog = new CatalogStub(Entries());
        await ProvinceSeeder.SeedAsync(db, catalog);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new ProvinceController(
            db, catalog, cache, NullLogger<ProvinceController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        await controller.Map(CancellationToken.None);
        controller.Request.Headers.IfNoneMatch = controller.Response.Headers.ETag;

        var conditional = await controller.Map(CancellationToken.None);

        Assert.Equal(StatusCodes.Status304NotModified,
            Assert.IsType<StatusCodeResult>(conditional.Result).StatusCode);
    }

    private static IReadOnlyList<ProvinceCatalogEntry> Entries()
    {
        var entries = new List<ProvinceCatalogEntry>(ProvinceCatalog.ExpectedProvinceCount);
        for (var index = 0; index < ProvinceCatalog.ExpectedProvinceCount; index++)
        {
            var x = 20 + index * 0.1;
            var boundary = CreateBoundary(x);
            var capital = GeometryFactory.CreatePoint(new Coordinate(x + 0.04, 35.04));
            entries.Add(new ProvinceCatalogEntry(
                $"Province {index + 1:00}",
                $"Region {index % 7}",
                "#2563EB",
                $"Capital {index + 1:00}",
                capital,
                boundary,
                "osm",
                $"node/{index + 1}",
                $"relation/{index + 1}",
                new DateOnly(2026, 7, 23),
                "Geofabrik Turkey OSM snapshot"));
        }

        return entries;
    }

    private static MultiPolygon CreateBoundary(double x)
    {
        var polygon = GeometryFactory.CreatePolygon(
        [
            new Coordinate(x, 35),
            new Coordinate(x + 0.08, 35),
            new Coordinate(x + 0.08, 35.08),
            new Coordinate(x, 35.08),
            new Coordinate(x, 35),
        ]);
        return GeometryFactory.CreateMultiPolygon([polygon]);
    }

    private sealed class CatalogStub(IReadOnlyList<ProvinceCatalogEntry> entries) : IProvinceCatalog
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ProvinceCatalogEntry>> GetAsync(
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(entries);
        }
    }
}
