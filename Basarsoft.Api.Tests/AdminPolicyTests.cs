using System.Reflection;
using Basarsoft.Api.Controllers;
using Basarsoft.Api.Data;
using Basarsoft.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Basarsoft.Api.Tests;

// Pins the per-resource admin authorization: each admin controller must demand its own manage_*
// policy — the coarse "any management permission" AdminAccess policy on any of them would let e.g.
// a manage_pois holder create users and grant themselves the Admin role.
public class AdminPolicyTests
{
    [Theory]
    [InlineData(typeof(AdminUsersController), PermissionRequirement.ManageUsers)]
    [InlineData(typeof(AdminRolesController), PermissionRequirement.ManageRoles)]
    [InlineData(typeof(AdminPermissionsController), PermissionRequirement.ManagePermissions)]
    [InlineData(typeof(AdminPoiCategoriesController), PermissionRequirement.ManagePois)]
    [InlineData(typeof(AdminTransportationController), PermissionRequirement.ManageTransportAdmin)]
    public void AdminController_DemandsItsOwnResourcePolicy(Type controller, string expectedPolicy)
    {
        var authorize = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: false).ToList();

        var attribute = Assert.Single(authorize);
        Assert.Equal(expectedPolicy, attribute.Policy);
    }

    [Fact]
    public void PolicyNames_CoverEveryManagementPermission()
    {
        // The four policies and the four seeded manage_* permissions must stay 1:1 — a new
        // management permission without its own policy would silently fall back to nothing.
        var policyBackedPermissions = new[]
        {
            SeedData.ManageUsersPermission,
            SeedData.ManageRolesPermission,
            SeedData.ManagePermissionsPermission,
            SeedData.ManagePoisPermission,
            SeedData.ManageTransportAdminPermission,
        };

        Assert.Equal(
            SeedData.AdminPermissions.OrderBy(n => n),
            policyBackedPermissions.OrderBy(n => n));
    }
}
