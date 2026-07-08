using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Basarsoft.Api.Data;

// Seeds the initial RBAC data. Idempotent: safe to run on every startup — each step is guarded by an
// existence check so re-runs are no-ops. Runs at startup (not in a migration) so the bootstrap user can
// be looked up by username rather than hard-coding an id.
public static class AdminSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // 1) Ensure every English catalogue permission exists. IgnoreQueryFilters prevents duplicate
        // inserts when a seeded permission was soft-deleted; in that case, restore the seed row.
        var seedNames = SeedData.Permissions.Select(seed => seed.Name).ToList();
        var permissions = await db.Permissions
            .IgnoreQueryFilters()
            .Where(p => seedNames.Contains(p.Name))
            .ToListAsync();

        var permissionsChanged = false;
        foreach (var (name, description) in SeedData.Permissions)
        {
            var permission = permissions.FirstOrDefault(p => p.Name == name);
            if (permission is null)
            {
                db.Permissions.Add(new Permission { Name = name, Description = description });
                permissionsChanged = true;
                continue;
            }

            if (permission.Description != description || permission.IsDeleted || !permission.IsActive)
            {
                permission.Description = description;
                permission.IsDeleted = false;
                permission.IsActive = true;
                permissionsChanged = true;
            }
        }

        if (permissionsChanged)
            await db.SaveChangesAsync();

        // 2) Ensure the Admin role exists.
        var createdAdminRole = false;
        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == SeedData.AdminRoleName);
        if (adminRole is null)
        {
            adminRole = new Role { Name = SeedData.AdminRoleName, Description = SeedData.AdminRoleDescription };
            db.Roles.Add(adminRole);
            await db.SaveChangesAsync();
            createdAdminRole = true;
        }

        // 3) Give a newly created Admin role the full initial catalogue. After that, the admin panel
        // owns the role's permissions; startup must not silently undo permissions an admin removed.
        if (createdAdminRole)
        {
            var allPermIds = await db.Permissions.Select(p => p.Id).ToListAsync();
            if (allPermIds.Count > 0)
            {
                db.RolePermissions.AddRange(allPermIds.Select(pid =>
                    new RolePermission { RoleId = adminRole.Id, PermissionId = pid }));
                await db.SaveChangesAsync();
            }
        }

        // 4) Grant the Admin role to the bootstrap user, if that account exists.
        var bootstrap = await db.Users.FirstOrDefaultAsync(u => u.Username == SeedData.BootstrapAdminUsername);
        if (bootstrap is not null &&
            !await db.UserRoles.AnyAsync(ur => ur.UserId == bootstrap.Id && ur.RoleId == adminRole.Id))
        {
            db.UserRoles.Add(new UserRole { UserId = bootstrap.Id, RoleId = adminRole.Id });
            await db.SaveChangesAsync();
        }
    }

}
