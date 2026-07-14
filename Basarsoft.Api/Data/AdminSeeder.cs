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

        // Names inserted for the first time on THIS run. Only these get auto-granted to an existing
        // Admin role below — re-granting the whole catalogue would resurrect grants an admin removed.
        var newPermissionNames = new List<string>();

        var permissionsChanged = false;
        foreach (var (name, description) in SeedData.Permissions)
        {
            var permission = permissions.FirstOrDefault(p => p.Name == name);
            if (permission is null)
            {
                db.Permissions.Add(new Permission { Name = name, Description = description });
                newPermissionNames.Add(name);
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

        // 3b) When the catalogue grew (e.g. the POI module added add_poi/manage_pois), an EXISTING
        // Admin role gets just the brand-new permissions — a targeted grant, existence-checked per row,
        // that can't undo any removal an admin made to the older permissions.
        if (!createdAdminRole && newPermissionNames.Count > 0)
        {
            var newPermIds = await db.Permissions
                .Where(p => newPermissionNames.Contains(p.Name))
                .Select(p => p.Id)
                .ToListAsync();
            var alreadyGranted = await db.RolePermissions
                .Where(rp => rp.RoleId == adminRole.Id && newPermIds.Contains(rp.PermissionId))
                .Select(rp => rp.PermissionId)
                .ToListAsync();
            var missing = newPermIds.Except(alreadyGranted).ToList();
            if (missing.Count > 0)
            {
                db.RolePermissions.AddRange(missing.Select(pid =>
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

        // 5) POI module roles. Operator gets add_poi only at creation time (the admin panel owns the
        // set afterwards, same rule as Admin); Viewer is deliberately permission-free (view-only).
        var operatorRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == SeedData.OperatorRoleName);
        if (operatorRole is null)
        {
            operatorRole = new Role
            {
                Name = SeedData.OperatorRoleName,
                Description = SeedData.OperatorRoleDescription,
            };
            db.Roles.Add(operatorRole);
            await db.SaveChangesAsync();

            var operatorPermIds = await db.Permissions
                .Where(p => SeedData.OperatorPermissions.Contains(p.Name))
                .Select(p => p.Id)
                .ToListAsync();
            if (operatorPermIds.Count > 0)
            {
                db.RolePermissions.AddRange(operatorPermIds.Select(pid =>
                    new RolePermission { RoleId = operatorRole.Id, PermissionId = pid }));
                await db.SaveChangesAsync();
            }
        }

        if (!await db.Roles.AnyAsync(r => r.Name == SeedData.ViewerRoleName))
        {
            db.Roles.Add(new Role
            {
                Name = SeedData.ViewerRoleName,
                Description = SeedData.ViewerRoleDescription,
            });
            await db.SaveChangesAsync();
        }
    }

}
