using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Basarsoft.Api.Controllers;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Basarsoft.Api.Tests;

public class PoiIconCatalogTests
{
    private static readonly string[] ExpectedKeys =
    [
        "pin",
        "food",
        "coffee",
        "bakery",
        "health",
        "pharmacy",
        "shopping",
        "culture",
        "museum",
        "hotel",
        "services",
        "bank",
        "fuel",
        "transport",
        "airport",
        "education",
        "nature",
        "sports",
        "mail",
        "government",
    ];

    [Fact]
    public void Catalog_IsTheAuthoritativeTwentyKeyAllowlist()
    {
        Assert.Equal("pin", PoiIconCatalog.DefaultIconKey);
        Assert.Equal(ExpectedKeys, PoiIconCatalog.All.Select(icon => icon.Key));
        Assert.Equal(ExpectedKeys.Length,
            PoiIconCatalog.All.Select(icon => icon.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(PoiIconCatalog.All, icon => Assert.False(string.IsNullOrWhiteSpace(icon.Label)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("food", "food")]
    [InlineData("  AIRPORT  ", "airport")]
    [InlineData("Government", "government")]
    public void TryNormalize_AcceptsInheritanceAndCanonicalizesAllowlistedKeys(
        string? input,
        string? expected)
    {
        Assert.True(PoiIconCatalog.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("../pin")]
    [InlineData("pin.svg")]
    [InlineData("food<script>")]
    public void TryNormalize_RejectsAnythingOutsideTheAllowlist(string input)
    {
        Assert.False(PoiIconCatalog.TryNormalize(input, out var normalized));
        Assert.Null(normalized);
    }

    [Theory]
    [InlineData(null, "pin")]
    [InlineData("", "pin")]
    [InlineData("unknown", "pin")]
    [InlineData("  Museum ", "museum")]
    public void NormalizeOrDefault_AlwaysReturnsARenderableKey(string? input, string expected)
    {
        Assert.Equal(expected, PoiIconCatalog.NormalizeOrDefault(input));
    }

    [Fact]
    public void PoiEffectiveIcon_UsesNearestAncestorThenPinFallback()
    {
        var categories = new Dictionary<int, PoiCategory>
        {
            [1] = Category(1, "Culture & Tourism", null, "culture"),
            [2] = Category(2, "Historical Site", 1, null),
            [3] = Category(3, "Museum", 2, "museum"),
            [4] = Category(4, "Unstyled Root", null, null),
            [5] = Category(5, "Unstyled Child", 4, null),
        };

        Assert.Equal("culture", ResolveEffectiveIcon(2, categories));
        Assert.Equal("museum", ResolveEffectiveIcon(3, categories));
        Assert.Equal("pin", ResolveEffectiveIcon(5, categories));
        Assert.Equal("pin", ResolveEffectiveIcon(999, categories));
    }

    [Fact]
    public async Task InvalidCategoryIcon_MapsToStable400Contract()
    {
        var controller = new AdminPoiCategoriesController(
            new InvalidIconCategoryService(),
            NullLogger<AdminPoiCategoriesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(JwtRegisteredClaimNames.Sub, "1")],
                        authenticationType: "test")),
                },
            },
        };

        var result = await controller.Create(new PoiCategorySaveRequest
        {
            Name = "Unsafe icon attempt",
            IconKey = "../pin",
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(badRequest.Value));
        Assert.Equal("invalid_icon_key", body.RootElement.GetProperty("code").GetString());
    }

    private static PoiCategory Category(
        int id,
        string name,
        int? parentId,
        string? iconKey) =>
        new()
        {
            Id = id,
            Name = name,
            ParentId = parentId,
            IconKey = iconKey,
            UserId = 1,
        };

    private static string ResolveEffectiveIcon(
        int categoryId,
        IReadOnlyDictionary<int, PoiCategory> categories)
    {
        var method = typeof(PoiService).GetMethod(
            "EffectiveIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, [categoryId, categories]));
    }

    private sealed class InvalidIconCategoryService : IPoiCategoryService
    {
        public Task<PoiCategoryWriteResult> CreateAsync(
            PoiCategorySaveRequest request,
            int userId)
        {
            Assert.False(PoiIconCatalog.TryNormalize(request.IconKey, out _));
            Assert.Equal(1, userId);
            return Task.FromResult(PoiCategoryWriteResult.InvalidIcon);
        }

        public Task<IReadOnlyList<PoiCategoryResponse>> ListAsync() =>
            throw new NotSupportedException();

        public Task<PoiCategoryWriteResult> UpdateAsync(
            int id,
            PoiCategorySaveRequest request,
            int userId) =>
            throw new NotSupportedException();

        public Task<PoiCategoryWriteStatus> DeleteAsync(int id, int userId) =>
            throw new NotSupportedException();
    }
}
