namespace Basarsoft.Api.Security;

// A shape's create, update, and delete operations all use the same permission. Keeping the mapping
// here prevents one write endpoint from accidentally enforcing a weaker rule than the others and
// gives contract tests a small, stable policy surface to verify.
public static class GeometryWritePermissions
{
    public static string? ForType(string? geometryType)
    {
        if (geometryType is null)
            return null;

        if (geometryType.Equals("point", StringComparison.OrdinalIgnoreCase))
            return "add_point";
        if (geometryType.Equals("line", StringComparison.OrdinalIgnoreCase))
            return "add_line";
        if (geometryType.Equals("polygon", StringComparison.OrdinalIgnoreCase))
            return "add_polygon";

        return null;
    }
}
