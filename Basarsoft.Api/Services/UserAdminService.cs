using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;
using BC = BCrypt.Net.BCrypt;

namespace Basarsoft.Api.Services;

public class UserAdminService : IUserAdminService
{
    private readonly AppDbContext _db;

    public UserAdminService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AdminUserResponse>> ListAsync()
    {
        var users = await _db.Users.OrderBy(u => u.Id).ToListAsync();

        // All user->role links pointing at non-deleted roles, grouped per user.
        var links = await (from ur in _db.UserRoles
                           join r in _db.Roles on ur.RoleId equals r.Id
                           select new { ur.UserId, RoleId = r.Id, r.Name }).ToListAsync();
        var byUser = links.GroupBy(x => x.UserId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<RoleSummary>)g.OrderBy(x => x.Name)
                .Select(x => new RoleSummary { Id = x.RoleId, Name = x.Name }).ToList());

        return users
            .Select(u => ToResponse(u, byUser.GetValueOrDefault(u.Id) ?? Array.Empty<RoleSummary>()))
            .ToList();
    }

    public async Task<AdminUserResponse?> GetAsync(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return null;

        return ToResponse(user, await GetRoleSummariesAsync(id));
    }

    public async Task<(AdminWriteStatus Status, AdminUserResponse? User)> CreateAsync(AdminUserCreateRequest request)
    {
        var username = request.Username.Trim();
        if (await _db.Users.AnyAsync(u => u.Username == username))
            return (AdminWriteStatus.Conflict, null);

        var user = new User
        {
            Username = username,
            PasswordHash = BC.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        if (request.RoleIds.Count > 0)
            await SetRolesAsync(user.Id, request.RoleIds);

        return (AdminWriteStatus.Ok, await GetAsync(user.Id));
    }

    public async Task<(AdminWriteStatus Status, AdminUserResponse? User)> UpdateAsync(int id, AdminUserUpdateRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return (AdminWriteStatus.NotFound, null);

        var username = request.Username.Trim();
        if (await _db.Users.AnyAsync(u => u.Id != id && u.Username == username))
            return (AdminWriteStatus.Conflict, null);

        user.Username = username;
        user.IsActive = request.IsActive;
        if (!string.IsNullOrWhiteSpace(request.NewPassword))
            user.PasswordHash = BC.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();

        return (AdminWriteStatus.Ok, await GetAsync(id));
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return false;

        _db.UserRoles.RemoveRange(_db.UserRoles.Where(ur => ur.UserId == id));
        _db.UserPermissions.RemoveRange(_db.UserPermissions.Where(up => up.UserId == id));
        user.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetRolesAsync(int userId, IReadOnlyList<int> roleIds)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId))
            return false;

        var desired = (await _db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Id).ToListAsync())
            .ToHashSet();

        var current = await _db.UserRoles.Where(ur => ur.UserId == userId).ToListAsync();
        var currentIds = current.Select(ur => ur.RoleId).ToHashSet();

        _db.UserRoles.RemoveRange(current.Where(ur => !desired.Contains(ur.RoleId)));
        foreach (var rid in desired.Where(rid => !currentIds.Contains(rid)))
            _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = rid });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetDirectPermissionsAsync(int userId, IReadOnlyList<int> permissionIds)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId))
            return false;

        var desired = (await _db.Permissions.Where(p => permissionIds.Contains(p.Id)).Select(p => p.Id).ToListAsync())
            .ToHashSet();

        var current = await _db.UserPermissions.Where(up => up.UserId == userId).ToListAsync();
        var currentIds = current.Select(up => up.PermissionId).ToHashSet();

        _db.UserPermissions.RemoveRange(current.Where(up => !desired.Contains(up.PermissionId)));
        foreach (var pid in desired.Where(pid => !currentIds.Contains(pid)))
            _db.UserPermissions.Add(new UserPermission { UserId = userId, PermissionId = pid });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyList<EffectivePermissionResponse>?> GetEffectivePermissionsAsync(int userId)
    {
        var data = await ResolveAsync(userId);
        return data?.Permissions;
    }

    public async Task<bool> IsAdminAsync(int userId)
    {
        var data = await ResolveAsync(userId);
        if (data is null)
            return false;

        return data.Permissions.Any(p => p.Source != "none" && SeedData.AdminPermissions.Contains(p.Name));
    }

    public async Task<bool> HasPermissionAsync(int userId, string permissionName)
    {
        var data = await ResolveAsync(userId);
        return data?.Permissions.Any(p => p.Source != "none" && p.Name == permissionName) ?? false;
    }

    public async Task<MeResponse?> GetMeAsync(int userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return null;

        var data = await ResolveAsync(userId);
        var granted = data!.Permissions.Where(p => p.Source != "none").Select(p => p.Name).ToList();

        return new MeResponse
        {
            Id = user.Id,
            Username = user.Username,
            IsAdmin = granted.Any(SeedData.AdminPermissions.Contains),
            Roles = data.RoleNames,
            Permissions = granted,
        };
    }

    // ---- helpers ----

    private static AdminUserResponse ToResponse(User u, IReadOnlyList<RoleSummary> roles) => new()
    {
        Id = u.Id,
        Username = u.Username,
        IsActive = u.IsActive,
        CreatedAt = u.CreatedAt,
        ModifiedDate = u.ModifiedDate,
        Roles = roles,
    };

    private Task<List<RoleSummary>> GetRoleSummariesAsync(int userId) =>
        (from ur in _db.UserRoles
         join r in _db.Roles on ur.RoleId equals r.Id
         where ur.UserId == userId
         orderby r.Name
         select new RoleSummary { Id = r.Id, Name = r.Name }).ToListAsync();

    // Builds the effective-permission view for a user in one pass. Returns null if the user is missing.
    // All queries run sequentially on the single DbContext (never Task.WhenAll — one context isn't
    // thread-safe). Role/permission query filters drop soft-deleted rows automatically via the joins.
    private async Task<EffectiveData?> ResolveAsync(int userId)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId))
            return null;

        // Names of the roles the user holds.
        var roleNames = await (from ur in _db.UserRoles
                               join r in _db.Roles on ur.RoleId equals r.Id
                               where ur.UserId == userId
                               orderby r.Name
                               select r.Name).ToListAsync();

        // (permissionId, roleName) for every permission reachable through one of the user's roles.
        var inheritedPairs = await (from ur in _db.UserRoles
                                    join rp in _db.RolePermissions on ur.RoleId equals rp.RoleId
                                    join r in _db.Roles on ur.RoleId equals r.Id
                                    join p in _db.Permissions on rp.PermissionId equals p.Id
                                    where ur.UserId == userId
                                    select new { rp.PermissionId, RoleName = r.Name }).ToListAsync();
        // If several roles grant the same permission, show one deterministic role name.
        var inheritedByPerm = inheritedPairs
            .GroupBy(x => x.PermissionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName).OrderBy(n => n).First());

        // Permissions granted directly to the user.
        var directIds = (await (from up in _db.UserPermissions
                                join p in _db.Permissions on up.PermissionId equals p.Id
                                where up.UserId == userId
                                select up.PermissionId).ToListAsync()).ToHashSet();

        // The whole catalogue, annotated. Role source wins over direct (mentor's rule).
        var catalogue = await _db.Permissions.OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Name, p.Description }).ToListAsync();

        var perms = catalogue.Select(p =>
        {
            var inherited = inheritedByPerm.TryGetValue(p.Id, out var roleName);
            var direct = directIds.Contains(p.Id);
            return new EffectivePermissionResponse
            {
                PermissionId = p.Id,
                Name = p.Name,
                Description = p.Description,
                Source = inherited ? "role" : direct ? "direct" : "none",
                RoleName = inherited ? roleName : null,
                IsInherited = inherited,
                IsDirect = direct,
            };
        }).ToList();

        return new EffectiveData(roleNames, perms);
    }

    private sealed record EffectiveData(
        IReadOnlyList<string> RoleNames,
        IReadOnlyList<EffectivePermissionResponse> Permissions);
}
