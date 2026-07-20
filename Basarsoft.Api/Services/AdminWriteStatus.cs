namespace Basarsoft.Api.Services;

// Outcome of an admin create/update/delete that can fail on a missing target, a duplicate name, or
// because the write would remove the system's last admin. Controllers map Ok -> 200/204,
// NotFound -> 404, Conflict + LastAdmin -> 409 (with distinct messages).
public enum AdminWriteStatus
{
    Ok,
    NotFound,
    Conflict,

    // The write would leave no active user holding any management permission — nobody could ever
    // open the admin panel again — so it is refused.
    LastAdmin,
}
