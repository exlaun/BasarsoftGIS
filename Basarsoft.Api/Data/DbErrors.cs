using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Basarsoft.Api.Data;

// Translates provider-specific database failures into questions services can ask. Unique violations
// are the one case today: the username pre-checks can race under concurrency, and the partial unique
// index on users.username is the backstop that turns the losing insert into a clean conflict.
public static class DbErrors
{
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}
