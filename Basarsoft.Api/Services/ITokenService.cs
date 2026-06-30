using Basarsoft.Api.DTOs;
using Basarsoft.Api.Models;

namespace Basarsoft.Api.Services;

// Builds a signed JWT (and its metadata) for an authenticated user.
public interface ITokenService
{
    AuthResponse CreateToken(User user);
}
