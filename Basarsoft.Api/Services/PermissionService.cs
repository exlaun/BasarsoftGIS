using Basarsoft.Api.Data;
using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Basarsoft.Api.Services;

public class PermissionService : IPermissionService
{
    private readonly AppDbContext _db;

    public PermissionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PermissionResponse>> ListAsync()
    {
        return await _db.Permissions
            .OrderBy(p => p.Id)
            .Select(p => new PermissionResponse { Id = p.Id, Name = p.Name, Description = p.Description })
            .ToListAsync();
    }

    public async Task<PermissionResponse?> CreateAsync(PermissionCreateRequest request)
    {
        var name = request.Name.Trim();
        if (await _db.Permissions.AnyAsync(p => p.Name == name))
            return null;

        var permission = new Permission { Name = name, Description = request.Description };
        _db.Permissions.Add(permission);
        await _db.SaveChangesAsync();

        return new PermissionResponse
        {
            Id = permission.Id,
            Name = permission.Name,
            Description = permission.Description,
        };
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var permission = await _db.Permissions.FirstOrDefaultAsync(p => p.Id == id);
        if (permission is null)
            return false;

        // Drop the grant links so no role/user keeps a dangling reference, then soft-delete the row.
        var roleLinks = _db.RolePermissions.Where(rp => rp.PermissionId == id);
        var userLinks = _db.UserPermissions.Where(up => up.PermissionId == id);
        _db.RolePermissions.RemoveRange(roleLinks);
        _db.UserPermissions.RemoveRange(userLinks);
        permission.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }
}
