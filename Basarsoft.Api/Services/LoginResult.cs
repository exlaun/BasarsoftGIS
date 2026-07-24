using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    Disabled,
}

public record LoginResult(LoginStatus Status, AuthResponse? Response)
{
    public static readonly LoginResult InvalidCredentials = new(LoginStatus.InvalidCredentials, null);
    public static readonly LoginResult Disabled = new(LoginStatus.Disabled, null);

    public static LoginResult Ok(AuthResponse response) => new(LoginStatus.Success, response);
}
