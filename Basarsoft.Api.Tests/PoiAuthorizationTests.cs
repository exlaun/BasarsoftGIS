using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Basarsoft.Api.Controllers;
using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Basarsoft.Api.Tests;

public class PoiAuthorizationTests
{
    [Theory]
    [InlineData(13)] // Operator
    [InlineData(17)] // Viewer
    [InlineData(18)] // Ordinary user
    public async Task Create_ReadOnlyUser_IsForbiddenBeforeServiceCall(int userId)
    {
        var pois = new RecordingPoiService();
        var controller = NewController(userId, new PermissionStub(), pois);

        var result = await controller.Create(new PoiCreateRequest());

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Equal((userId, "add_poi"), controller.Permissions.LastCheck);
        Assert.Equal(0, pois.CreateCalls);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(17)]
    [InlineData(18)]
    public async Task Delete_ReadOnlyUser_IsForbiddenEvenForLegacyOwnership(int userId)
    {
        var pois = new RecordingPoiService();
        var controller = NewController(userId, new PermissionStub(), pois);

        var result = await controller.Delete(42);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal((userId, SeedData.ManagePoisPermission), controller.Permissions.LastCheck);
        Assert.Equal(0, pois.DeleteCalls);
    }

    [Fact]
    public async Task Delete_ManagePoisHolder_RemainsAllowed()
    {
        var pois = new RecordingPoiService();
        var permissions = new PermissionStub(SeedData.ManagePoisPermission);
        var controller = NewController(1, permissions, pois);

        var result = await controller.Delete(42);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(1, pois.DeleteCalls);
    }

    private static TestPoiController NewController(
        int userId,
        PermissionStub permissions,
        RecordingPoiService pois)
    {
        var controller = new TestPoiController(
            pois,
            null!,
            null!,
            permissions,
            NullLogger<PoiController>.Instance,
            permissions)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())], "test")),
                },
            },
        };
        return controller;
    }

    private sealed class TestPoiController : PoiController
    {
        public TestPoiController(
            IPoiService pois,
            IPoiCategoryService categories,
            IGeoServerReadService geoServer,
            IUserAdminService userAdmin,
            ILogger<PoiController> logger,
            PermissionStub permissions)
            : base(pois, categories, geoServer, userAdmin, logger)
        {
            Permissions = permissions;
        }

        public PermissionStub Permissions { get; }
    }

    private sealed class RecordingPoiService : IPoiService
    {
        public int CreateCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task<PoiWriteResult> CreateAsync(PoiCreateRequest request, int userId)
        {
            CreateCalls++;
            return Task.FromResult(PoiWriteResult.InvalidGeometry);
        }

        public Task<bool> DeleteAsync(int id, int userId, bool isAdmin)
        {
            DeleteCalls++;
            return Task.FromResult(true);
        }
    }

    private sealed class PermissionStub(params string[] granted) : IUserAdminService
    {
        public (int UserId, string Permission)? LastCheck { get; private set; }

        public Task<bool> HasPermissionAsync(int userId, string permissionName)
        {
            LastCheck = (userId, permissionName);
            return Task.FromResult(granted.Contains(permissionName));
        }

        public Task<IReadOnlyList<AdminUserResponse>> ListAsync() => throw new NotSupportedException();
        public Task<AdminUserResponse?> GetAsync(int id) => throw new NotSupportedException();
        public Task<(AdminWriteStatus Status, AdminUserResponse? User)> CreateAsync(AdminUserCreateRequest request) => throw new NotSupportedException();
        public Task<(AdminWriteStatus Status, AdminUserResponse? User)> UpdateAsync(int id, AdminUserUpdateRequest request) => throw new NotSupportedException();
        public Task<AdminWriteStatus> DeleteAsync(int id) => throw new NotSupportedException();
        public Task<bool> SetRolesAsync(int userId, IReadOnlyList<int> roleIds) => throw new NotSupportedException();
        public Task<IReadOnlyList<EffectivePermissionResponse>?> GetEffectivePermissionsAsync(int userId) => throw new NotSupportedException();
        public Task<bool> SetDirectPermissionsAsync(int userId, IReadOnlyList<int> permissionIds) => throw new NotSupportedException();
        public Task<bool> IsAdminAsync(int userId) => throw new NotSupportedException();
        public Task<MeResponse?> GetMeAsync(int userId) => throw new NotSupportedException();
    }
}
