using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

public interface IUserAdminService
{
    // All users (non-deleted), each with the roles they hold.
    Task<IReadOnlyList<AdminUserResponse>> ListAsync();

    // One user with roles, or null if missing.
    Task<AdminUserResponse?> GetAsync(int id);

    // Creates a user (BCrypt-hashed password) and optionally grants roles. Conflict on a duplicate name.
    Task<(AdminWriteStatus Status, AdminUserResponse? User)> CreateAsync(AdminUserCreateRequest request);

    // Updates username/active flag and, when NewPassword is supplied, resets the password. NotFound if
    // missing, Conflict on a duplicate name.
    Task<(AdminWriteStatus Status, AdminUserResponse? User)> UpdateAsync(int id, AdminUserUpdateRequest request);

    // Soft-deletes a user and removes their role + direct-permission links. False if missing.
    Task<bool> DeleteAsync(int id);

    // Replaces the user's role set with the given ids (unknown ids ignored). False if missing.
    Task<bool> SetRolesAsync(int userId, IReadOnlyList<int> roleIds);

    // The full permission catalogue annotated with how (if at all) the user has each one: inherited from
    // a role, granted directly, or not granted. Null if the user is missing. This is the payload that
    // drives the inheritance-aware editor. See EffectivePermissionResponse.
    Task<IReadOnlyList<EffectivePermissionResponse>?> GetEffectivePermissionsAsync(int userId);

    // Replaces the user's DIRECT permission grants with the given ids (unknown ids ignored). Role-derived
    // permissions are unaffected. False if the user is missing.
    Task<bool> SetDirectPermissionsAsync(int userId, IReadOnlyList<int> permissionIds);

    // True if the user holds any management permission (may open the admin panel). False if missing.
    Task<bool> IsAdminAsync(int userId);

    // True if the user effectively holds one specific permission, whether inherited from a role or
    // granted directly. False if missing.
    Task<bool> HasPermissionAsync(int userId, string permissionName);

    // Identity + RBAC context for GET /api/auth/me. Null if the user is missing.
    Task<MeResponse?> GetMeAsync(int userId);
}
