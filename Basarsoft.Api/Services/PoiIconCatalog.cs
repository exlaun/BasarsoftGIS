using Basarsoft.Api.DTOs;

namespace Basarsoft.Api.Services;

// Authoritative POI icon allowlist shared by request validation, effective-icon resolution and the
// public catalog endpoint. Keys are stable API/storage values; labels may evolve independently.
public static class PoiIconCatalog
{
    public const string DefaultIconKey = "pin";

    public static IReadOnlyList<PoiIconResponse> All { get; } = new[]
    {
        Icon("pin", "Pin"),
        Icon("food", "Food & Drink"),
        Icon("coffee", "Coffee"),
        Icon("bakery", "Bakery"),
        Icon("health", "Health"),
        Icon("pharmacy", "Pharmacy"),
        Icon("shopping", "Shopping"),
        Icon("culture", "Culture & Tourism"),
        Icon("museum", "Museum"),
        Icon("hotel", "Hotel"),
        Icon("services", "Services"),
        Icon("bank", "Bank"),
        Icon("fuel", "Fuel"),
        Icon("transport", "Transport"),
        Icon("airport", "Airport"),
        Icon("education", "Education"),
        Icon("nature", "Nature & Recreation"),
        Icon("sports", "Sports"),
        Icon("mail", "Post Office"),
        Icon("government", "Government"),
    };

    private static readonly HashSet<string> Keys =
        All.Select(icon => icon.Key).ToHashSet(StringComparer.Ordinal);

    // Null/blank is the explicit inheritance value. Non-empty keys are trimmed and made canonical;
    // anything outside the 20-key catalog is rejected.
    public static bool TryNormalize(string? value, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = null;
            return true;
        }

        normalized = value.Trim().ToLowerInvariant();
        if (Keys.Contains(normalized))
            return true;

        normalized = null;
        return false;
    }

    // Database/view reads should always give renderers a usable key, even if external SQL or legacy
    // data bypassed the API validation.
    public static string NormalizeOrDefault(string? value) =>
        TryNormalize(value, out var normalized) && normalized is not null
            ? normalized
            : DefaultIconKey;

    private static PoiIconResponse Icon(string key, string label) => new()
    {
        Key = key,
        Label = label,
    };
}
