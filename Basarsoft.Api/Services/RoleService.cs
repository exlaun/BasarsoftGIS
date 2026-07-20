using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Basarsoft.Api.Services;

public class RoleService : IRoleService
{
    private readonly AppDbContext _db;

    public RoleService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<RoleResponse>> ListAsync()
    {
        var roles = await _db.Roles.OrderBy(r => r.Id).ToListAsync();

        // Only links pointing at still-existing permissions (the join to Permissions applies its
        // !IsDeleted filter automatically).
        var links = await (from rp in _db.RolePermissions
                           join p in _db.Permissions on rp.PermissionId equals p.Id
                           select new { rp.RoleId, rp.PermissionId }).ToListAsync();
        var byRole = links.GroupBy(x => x.RoleId)
                          .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(x => x.PermissionId).ToList());

        return roles.Select(r => new RoleResponse
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            PermissionIds = byRole.TryGetValue(r.Id, out var ids) ? ids : Array.Empty<int>(),
        }).ToList();
    }

    public async Task<RoleResponse?> GetAsync(int id)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
            return null;

        var permissionIds = await (from rp in _db.RolePermissions
                                   join p in _db.Permissions on rp.PermissionId equals p.Id
                                   where rp.RoleId == id
                                   select rp.PermissionId).ToListAsync();

        return new RoleResponse
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description,
            PermissionIds = permissionIds,
        };
    }

    public async Task<RoleResponse?> CreateAsync(RoleCreateRequest request)
    {
        var name = request.Name.Trim();
        if (await _db.Roles.AnyAsync(r => r.Name == name))
            return null;

        var role = new Role { Name = name, Description = request.Description };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        return new RoleResponse { Id = role.Id, Name = role.Name, Description = role.Description };
    }

    public async Task<(AdminWriteStatus Status, RoleResponse? Role)> UpdateAsync(int id, RoleUpdateRequest request)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
            return (AdminWriteStatus.NotFound, null);

        var name = request.Name.Trim();
        if (await _db.Roles.AnyAsync(r => r.Id != id && r.Name == name))
            return (AdminWriteStatus.Conflict, null);

        role.Name = name;
        role.Description = request.Description;
        await _db.SaveChangesAsync();

        var updated = await GetAsync(id);
        return (AdminWriteStatus.Ok, updated);
    }

    public async Task<AdminWriteStatus> DeleteAsync(int id)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
            return AdminWriteStatus.NotFound;

        // The role's assignments die with it (links removed below), so first make sure some active
        // user would still hold a management permission WITHOUT this role — deleting e.g. the Admin
        // role otherwise locks everyone out of the admin panel for good. One round-trip of
        // correlated EXISTS subqueries, mirroring UserAdminService.OtherAdminExistsAsync.
        // Array-typed names: EF/Npgsql translates Contains over an array, not over IReadOnlySet.
        var adminNames = SeedData.AdminPermissions.ToArray();
        var adminSurvives = await _db.Users.AnyAsync(u => u.IsActive &&
            (_db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId != id
                 && _db.Roles.Any(r => r.Id == ur.RoleId)
                 && _db.RolePermissions.Any(rp => rp.RoleId == ur.RoleId
                     && _db.Permissions.Any(p => p.Id == rp.PermissionId
                         && adminNames.Contains(p.Name))))
             || _db.UserPermissions.Any(up => up.UserId == u.Id
                 && _db.Permissions.Any(p => p.Id == up.PermissionId
                     && adminNames.Contains(p.Name)))));
        if (!adminSurvives)
            return AdminWriteStatus.LastAdmin;

        // Remove the role's grant links + every user's assignment of it, so nobody keeps effective
        // permissions from a deleted role, then soft-delete the row itself.
        _db.RolePermissions.RemoveRange(_db.RolePermissions.Where(rp => rp.RoleId == id));
        _db.UserRoles.RemoveRange(_db.UserRoles.Where(ur => ur.RoleId == id));
        role.IsDeleted = true;
        await _db.SaveChangesAsync();
        return AdminWriteStatus.Ok;
    }

    public async Task<bool> SetPermissionsAsync(int roleId, IReadOnlyList<int> permissionIds)
    {
        if (!await _db.Roles.AnyAsync(r => r.Id == roleId))
            return false;

        // Keep only ids that point at real, non-deleted permissions (guards against stale/garbage input).
        var validIds = await _db.Permissions
            .Where(p => permissionIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync();
        var desired = validIds.ToHashSet();

        var current = await _db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();
        var currentIds = current.Select(rp => rp.PermissionId).ToHashSet();

        _db.RolePermissions.RemoveRange(current.Where(rp => !desired.Contains(rp.PermissionId)));
        foreach (var pid in desired.Where(pid => !currentIds.Contains(pid)))
            _db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = pid });

        await _db.SaveChangesAsync();
        return true;
    }
}
