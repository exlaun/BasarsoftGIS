using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

public interface IPermissionService
{
    // The whole permission catalogue (non-deleted), ordered by id.
    Task<IReadOnlyList<PermissionResponse>> ListAsync();

    // Adds a permission. Returns null if the name is already taken.
    Task<PermissionResponse?> CreateAsync(PermissionCreateRequest request);

    // Soft-deletes a permission and removes its role/user grant links. False if it doesn't exist.
    Task<bool> DeleteAsync(int id);
}
