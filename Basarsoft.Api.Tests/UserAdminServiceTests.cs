using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Basarsoft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Basarsoft.Api.Tests;

// Service-level tests for effective-permission resolution (the hot-path boolean checks and the
// "role wins over direct" labeling) and the last-admin lockout guards, on the in-memory EF
// provider. Relational-only concerns (the unique username index, sequences) stay covered by the
// curl/e2e checks — this provider ignores them.
public class UserAdminServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // Minimal RBAC world: permission ids 1..3, user ids as given. Ids are explicit because the
    // in-memory provider does not run the sequence-backed defaults the real database uses.
    private static async Task SeedCatalogueAsync(AppDbContext db)
    {
        db.Permissions.AddRange(
            new Permission { Id = 1, Name = SeedData.ManageUsersPermission, Description = "u" },
            new Permission { Id = 2, Name = SeedData.ManagePoisPermission, Description = "p" },
            new Permission { Id = 3, Name = "add_point", Description = "d" });
        await db.SaveChangesAsync();
    }

    private static User NewUser(int id, bool isActive = true) => new()
    {
        Id = id,
        Username = $"user{id}",
        PasswordHash = "x",
        CreatedAt = DateTime.UtcNow,
        IsActive = isActive,
    };

    [Fact]
    public async Task HasPermission_GrantedThroughRole_IsTrue()
    {
        await using var db = NewDb();
        await SeedCatalogueAsync(db);
        db.Users.Add(NewUser(1));
        db.Roles.Add(new Role { Id = 1, Name = "Admin" });
        db.RolePermissions.Add(new RolePermission { Id = 1, RoleId = 1, PermissionId = 1 });
        db.UserRoles.Add(new UserRole { Id = 1, UserId = 1, RoleId = 1 });
        await db.SaveChangesAsync();

        var service = new UserAdminService(db);
        Assert.True(await service.HasPermissionAsync(1, SeedData.ManageUsersPermission));
        Assert.False(await service.HasPermissionAsync(1, "add_point"));
    }

    [Fact]
    public async Task HasPermission_GrantedDirectly_IsTrue()
    {
        await using var db = NewDb();
        await SeedCatalogueAsync(db);
        db.Users.Add(NewUser(1));
        db.UserPermissions.Add(new UserPermission { Id = 1, UserId = 1, PermissionId = 3 });
        await db.SaveChangesAsync();

        var service = new UserAdminService(db);
        Assert.True(await service.HasPermissionAsync(1, "add_point"));
    }

    [Fact]
    public async Task HasPermission_DeactivatedUser_IsFalse()
    {
        await using var db = NewDb();
        await SeedCatalogueAsync(db);
        db.Users.Add(NewUser(1, isActive: false));
        db.UserPermissions.Add(new UserPermission { Id = 1, UserId = 1, PermissionId = 1 });
        await db.SaveChangesAsync();

        var service = new UserAdminService(db);
        // Disabling an account revokes its powers immediately, not at token expiry.
        Assert.False(await service.HasPermissionAsync(1, SeedData.ManageUsersPermission));
        Assert.False(await service.IsAdminAsync(1));
        Assert.Null(await service.GetMeAsync(1));
    }

    [Fact]
    public async Task HasPermission_ThroughSoftDeletedRole_IsFalse()
    {
        await using var db = NewDb();
        await SeedCatalogueAsync(db);
        db.Users.Add(NewUser(1));
        db.Roles.Add(new Role { Id = 1, Name = "Ghost", IsDeleted = true });
        db.RolePermissions.Add(new RolePermission { Id = 1, RoleId = 1, PermissionId = 1 });
        db.UserRoles.Add(new UserRole { Id = 1, UserId = 1, RoleId = 1 });
        await db.SaveChangesAsync();

        var service = new UserAdminService(db);
        // The join runs through the filtered Roles set, so a deleted role grants nothing.
        Assert.False(await service.HasPermissionAsync(1, SeedData.ManageUsersPermission));
    }

    [Fact]
    public async Task IsAdmin_AnySingleManagementPermission_IsTrue()
    {
        await using var db = NewDb();
        await SeedCatalogueAsync(db);
        db.Users.Add(NewUser(1));
        db.UserPermissions.Add(new UserPermission { Id = 1, UserId = 1, PermissionId = 2 });
        await db.SaveChangesAsync();

        var service = new UserAdminService(db);
        Assert.True(await service.IsAdminAsync(1));
    }

    [Fact]
    public async Task EffectivePermissions_RoleSourceWinsOverDirect()
    {
        await using var db = NewDb();
        await SeedCatalogueAsync(db);
        db.Users.Add(NewUser(1));
        db.Roles.Add(new Role { Id = 1, Name = "Admin" });
        db.RolePermissions.Add(new RolePermission { Id = 1, RoleId = 1, PermissionId = 1 });
        db.UserRoles.Add(new UserRole { Id = 1, UserId = 1, RoleId = 1 });
        db.UserPermissions.Add(new UserPermission { Id = 1, UserId = 1, PermissionId = 1 });
        await db.SaveChangesAsync();

        var service = new UserAdminService(db);
        var perms = await service.GetEffectivePermissionsAsync(1);

        var manageUsers = Assert.Single(perms!, p => p.Name == SeedData.ManageUsersPermission);
        // Held both ways, but the mentor's rule labels the ROLE as the source.
        Assert.Equal("role", manageUsers.Source);
        Assert.True(manageUsers.IsInherited);
        Assert.True(manageUsers.IsDirect);
        Assert.Equal("Admin", manageUsers.RoleName);
    }

    [Fact]
    public async Task DeleteUser_LastActiveAdmin_IsRefused()
    {
        await using var db = NewDb();
        await SeedCatalogueAsync(db);
        db.Users.Add(NewUser(1));
        db.UserPermissions.Add(new UserPermission { Id = 1, UserId = 1, PermissionId = 1 });
        await db.SaveChangesAsync();

        var service = new UserAdminService(db);
        Assert.Equal(AdminWriteStatus.LastAdmin, await service.DeleteAsync(1));
        Assert.False((await db.Users.SingleAsync(u => u.Id == 1)).IsDeleted);
    }

    [Fact]
    public async Task DeleteUser_AnotherActiveAdminRemains_IsAllowed()
    {
        await using var db = NewDb();
        await SeedCatalogueAsync(db);
        db.Users.AddRange(NewUser(1), NewUser(2));
        db.UserPermissions.AddRange(
            new UserPermission { Id = 1, UserId = 1, PermissionId = 1 },
            new UserPermission { Id = 2, UserId = 2, PermissionId = 2 });
        await db.SaveChangesAsync();

        var service = new UserAdminService(db);
        Assert.Equal(AdminWriteStatus.Ok, await service.DeleteAsync(1));
    }

    [Fact]
    public async Task DeactivateUser_LastActiveAdmin_IsRefused()
    {
        await using var db = NewDb();
        await SeedCatalogueAsync(db);
        db.Users.Add(NewUser(1));
        db.UserPermissions.Add(new UserPermission { Id = 1, UserId = 1, PermissionId = 1 });
        await db.SaveChangesAsync();

        var service = new UserAdminService(db);
        var (status, _) = await service.UpdateAsync(1, new AdminUserUpdateRequest
        {
            Username = "user1",
            IsActive = false,
        });

        Assert.Equal(AdminWriteStatus.LastAdmin, status);
        Assert.True((await db.Users.SingleAsync(u => u.Id == 1)).IsActive);
    }
}
