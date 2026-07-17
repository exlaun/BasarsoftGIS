namespace Basarsoft.Api.DTOs;

// One supported POI marker glyph. The key is persisted on categories and the label is presentation
// metadata for the admin picker, keeping the server's allowlist authoritative.
public class PoiIconResponse
{
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;
}
