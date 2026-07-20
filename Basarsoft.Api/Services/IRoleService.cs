using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

public interface IRoleService
{
    // All roles (non-deleted), each with the ids of the permissions it grants.
    Task<IReadOnlyList<RoleResponse>> ListAsync();

    // One role with its permission ids, or null if it doesn't exist.
    Task<RoleResponse?> GetAsync(int id);

    // Creates a role. Returns null if the name is already taken.
    Task<RoleResponse?> CreateAsync(RoleCreateRequest request);

    // Updates a role's name/description. NotFound if missing, Conflict on a duplicate name.
    Task<(AdminWriteStatus Status, RoleResponse? Role)> UpdateAsync(int id, RoleUpdateRequest request);

    // Soft-deletes a role and removes its permission + user-assignment links. NotFound if missing;
    // LastAdmin if losing this role's grants would leave no active admin-permission holder.
    Task<AdminWriteStatus> DeleteAsync(int id);

    // Replaces the role's permission set with the given ids (unknown ids are ignored). False if missing.
    Task<bool> SetPermissionsAsync(int roleId, IReadOnlyList<int> permissionIds);
}
