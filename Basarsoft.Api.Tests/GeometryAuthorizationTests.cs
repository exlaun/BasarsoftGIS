using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.Controllers;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Security;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Basarsoft.Api.Tests;

public class GeometryAuthorizationTests
{
    [Theory]
    [InlineData("point", "add_point")]
    [InlineData("POINT", "add_point")]
    [InlineData("line", "add_line")]
    [InlineData("Line", "add_line")]
    [InlineData("polygon", "add_polygon")]
    [InlineData("POLYGON", "add_polygon")]
    public void WritePermissionMapping_UsesOnePermissionPerGeometryType(
        string geometryType,
        string expectedPermission)
    {
        Assert.Equal(expectedPermission, GeometryWritePermissions.ForType(geometryType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("poi")]
    [InlineData("multipolygon")]
    public void WritePermissionMapping_RejectsUnknownGeometryTypes(string? geometryType)
    {
        Assert.Null(GeometryWritePermissions.ForType(geometryType));
    }

    [Fact]
    public async Task Create_ViewerWithoutMatchingPermission_IsForbiddenBeforeServiceCall()
    {
        var (controller, geometry, permissions) = CreateViewerController();

        var result = await controller.Create("point", new GeometryCreateRequest
        {
            Name = "Read-only favorite",
            Wkt = "POINT(32.85 39.93)",
            Color = "#2563eb",
        });

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Equal((17, "add_point"), permissions.LastCheck);
        Assert.Equal(0, geometry.WriteCalls);
    }

    [Fact]
    public async Task Update_ViewerWithoutMatchingPermission_IsForbiddenBeforeServiceCall()
    {
        var (controller, geometry, permissions) = CreateViewerController();

        var result = await controller.Update("line", 3, new GeometryUpdateRequest
        {
            Name = "Attempted read-only edit",
        });

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Equal((17, "add_line"), permissions.LastCheck);
        Assert.Equal(0, geometry.WriteCalls);
    }

    [Fact]
    public async Task Delete_ViewerWithoutMatchingPermission_IsForbiddenBeforeServiceCall()
    {
        var (controller, geometry, permissions) = CreateViewerController();

        var result = await controller.Delete("polygon", 5);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal((17, "add_polygon"), permissions.LastCheck);
        Assert.Equal(0, geometry.WriteCalls);
    }

    private static (
        GeometryController Controller,
        RecordingGeometryService Geometry,
        DenyingUserAdminService Permissions) CreateViewerController()
    {
        var geometry = new RecordingGeometryService();
        var permissions = new DenyingUserAdminService();
        var controller = new GeometryController(
            geometry,
            new UnusedGeoServerReadService(),
            permissions,
            NullLogger<GeometryController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(JwtRegisteredClaimNames.Sub, "17")],
                        authenticationType: "test")),
                },
            },
        };
        return (controller, geometry, permissions);
    }

    private sealed class RecordingGeometryService : IGeometryService
    {
        public int WriteCalls { get; private set; }

        public Task<GeometryUpdateResult> CreateAsync(
            string type,
            GeometryCreateRequest request,
            int userId)
        {
            WriteCalls++;
            return Task.FromResult(GeometryUpdateResult.InvalidGeometry);
        }

        public Task<GeometryUpdateResult> UpdateAsync(
            string type,
            int id,
            GeometryUpdateRequest request,
            int userId)
        {
            WriteCalls++;
            return Task.FromResult(GeometryUpdateResult.NotFound);
        }

        public Task<DeleteStatus> DeleteAsync(string type, int id, int userId)
        {
            WriteCalls++;
            return Task.FromResult(DeleteStatus.NotFound);
        }

        public Task<IReadOnlyList<GeometryResponse>> ListAsync(string type, int userId) =>
            throw new NotSupportedException();

        public Task<AnalysisResponse?> AnalyzeAsync(string wkt, int userId) =>
            throw new NotSupportedException();

        public Task<GeometryQueryResponse?> QueryPageAsync(
            GeometryQueryRequest request,
            int userId) =>
            throw new NotSupportedException();
    }

    private sealed class DenyingUserAdminService : IUserAdminService
    {
        public (int UserId, string Permission)? LastCheck { get; private set; }

        public Task<bool> HasPermissionAsync(int userId, string permissionName)
        {
            LastCheck = (userId, permissionName);
            return Task.FromResult(false);
        }

        public Task<IReadOnlyList<AdminUserResponse>> ListAsync() =>
            throw new NotSupportedException();

        public Task<AdminUserResponse?> GetAsync(int id) =>
            throw new NotSupportedException();

        public Task<(AdminWriteStatus Status, AdminUserResponse? User)> CreateAsync(
            AdminUserCreateRequest request) =>
            throw new NotSupportedException();

        public Task<(AdminWriteStatus Status, AdminUserResponse? User)> UpdateAsync(
            int id,
            AdminUserUpdateRequest request) =>
            throw new NotSupportedException();

        public Task<AdminWriteStatus> DeleteAsync(int id) =>
            throw new NotSupportedException();

        public Task<bool> SetRolesAsync(int userId, IReadOnlyList<int> roleIds) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EffectivePermissionResponse>?> GetEffectivePermissionsAsync(int userId) =>
            throw new NotSupportedException();

        public Task<bool> SetDirectPermissionsAsync(
            int userId,
            IReadOnlyList<int> permissionIds) =>
            throw new NotSupportedException();

        public Task<bool> IsAdminAsync(int userId) =>
            throw new NotSupportedException();

        public Task<MeResponse?> GetMeAsync(int userId) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedGeoServerReadService : IGeoServerReadService
    {
        public Task<AllGeometryResponse> GetAllForUserAsync(int userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GeoServerImage> GetMapAsync(
            int userId,
            string bbox,
            int width,
            int height,
            string crs,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GeoServerImage> GetHeatmapAsync(
            int userId,
            string bbox,
            int width,
            int height,
            string crs,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PoiResponse>> GetPoisAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GeoServerImage> GetLocationHeatmapAsync(
            int analysisId,
            string bbox,
            int width,
            int height,
            string crs,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
