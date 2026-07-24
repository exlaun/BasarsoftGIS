using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.IO.Converters;

namespace Basarsoft.Api.Data;

// One source-backed province entry from Data/provinces.geojson. The boundary is persisted in
// tbl_province; the remaining reference metadata stays in the versioned catalog and powers the
// shared province/capital map layer without duplicating it across database columns.
public sealed record ProvinceCatalogEntry(
    string Name,
    string Region,
    string Color,
    string CapitalName,
    Point CapitalGeom,
    MultiPolygon Boundary,
    string SourceKey,
    string SourceId,
    string BoundarySourceId,
    DateOnly CapturedAt,
    string GeometrySource);

public interface IProvinceCatalog
{
    Task<IReadOnlyList<ProvinceCatalogEntry>> GetAsync(CancellationToken cancellationToken = default);
}

// Loads and validates the committed province catalog once per API process. Validation is deliberately
// strict: malformed provenance, duplicate names/ids, incomplete coverage, or a capital outside its
// province stops startup/seeding instead of quietly publishing misleading reference data.
public sealed class ProvinceCatalog : IProvinceCatalog
{
    public const int ExpectedProvinceCount = 81;
    private const int Srid = 4326;

    private static readonly JsonSerializerOptions GeoJsonOptions = CreateGeoJsonOptions();
    private static readonly Regex HexColor = new(
        "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly string _path;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private IReadOnlyList<ProvinceCatalogEntry>? _entries;

    public ProvinceCatalog()
        : this(Path.Combine(AppContext.BaseDirectory, "Data", "provinces.geojson"))
    {
    }

    // Public for focused tests and offline fixture validation commands.
    public ProvinceCatalog(string path)
    {
        _path = path;
    }

    public async Task<IReadOnlyList<ProvinceCatalogEntry>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (_entries is not null)
            return _entries;

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_entries is null)
                _entries = await LoadAsync(_path, cancellationToken);
            return _entries;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    internal static async Task<IReadOnlyList<ProvinceCatalogEntry>> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("type", out var collectionType) ||
            collectionType.GetString() != "FeatureCollection" ||
            !doc.RootElement.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("provinces.geojson is not a GeoJSON FeatureCollection.");
        }

        var entries = new List<ProvinceCatalogEntry>(features.GetArrayLength());
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("properties", out var properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Every province feature must have properties.");
            }

            var name = ReadRequiredText(properties, "name");
            var region = ReadRequiredText(properties, "region");
            var color = ReadRequiredText(properties, "color");
            var capitalName = ReadRequiredText(properties, "capitalName");
            var sourceKey = ReadRequiredText(properties, "sourceKey");
            var sourceId = ReadRequiredText(properties, "sourceId");
            var boundarySourceId = ReadRequiredText(properties, "boundarySourceId");
            var capturedAtText = ReadRequiredText(properties, "capturedAt");
            var geometrySource = ReadRequiredText(properties, "geometrySource");
            var longitude = ReadRequiredDouble(properties, "capitalLongitude");
            var latitude = ReadRequiredDouble(properties, "capitalLatitude");

            if (!HexColor.IsMatch(color))
                throw new InvalidOperationException($"Province '{name}' has invalid color '{color}'.");
            if (longitude is < -180 or > 180 || latitude is < -90 or > 90)
                throw new InvalidOperationException($"Province '{name}' has invalid capital coordinates.");
            if (!DateOnly.TryParseExact(
                    capturedAtText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var capturedAt))
            {
                throw new InvalidOperationException(
                    $"Province '{name}' has invalid capturedAt '{capturedAtText}'.");
            }

            if (!feature.TryGetProperty("geometry", out var geometryElement))
                throw new InvalidOperationException($"Province '{name}' has no boundary geometry.");

            var geometry = JsonSerializer.Deserialize<Geometry>(
                geometryElement.GetRawText(), GeoJsonOptions);
            if (geometry is null || geometry.IsEmpty)
                throw new InvalidOperationException($"Province '{name}' has an empty boundary.");
            if (!geometry.IsValid)
                geometry = GeometryFixer.Fix(geometry);
            if (!geometry.IsValid)
                throw new InvalidOperationException($"Province '{name}' has an invalid boundary.");

            var boundary = geometry switch
            {
                Polygon polygon => polygon.Factory.CreateMultiPolygon([polygon]),
                MultiPolygon multiPolygon => multiPolygon,
                _ => throw new InvalidOperationException(
                    $"Province '{name}' boundary is not a Polygon or MultiPolygon."),
            };
            boundary.SRID = Srid;

            var capital = boundary.Factory.CreatePoint(new Coordinate(longitude, latitude));
            capital.SRID = Srid;
            if (!boundary.Covers(capital))
            {
                throw new InvalidOperationException(
                    $"Capital '{capitalName}' is outside province '{name}'.");
            }

            entries.Add(new ProvinceCatalogEntry(
                name,
                region,
                color,
                capitalName,
                capital,
                boundary,
                sourceKey,
                sourceId,
                boundarySourceId,
                capturedAt,
                geometrySource));
        }

        if (entries.Count != ExpectedProvinceCount)
        {
            throw new InvalidOperationException(
                $"Province catalog must contain exactly {ExpectedProvinceCount} features; found {entries.Count}.");
        }

        var duplicateName = entries
            .GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName is not null)
            throw new InvalidOperationException($"Duplicate province name '{duplicateName.Key}'.");

        var duplicateSource = entries
            .GroupBy(entry => (entry.SourceKey, entry.SourceId))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSource is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate province source id '{duplicateSource.Key.SourceKey}:{duplicateSource.Key.SourceId}'.");
        }

        var duplicateBoundarySource = entries
            .GroupBy(entry => entry.BoundarySourceId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateBoundarySource is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate province boundary source id '{duplicateBoundarySource.Key}'.");
        }

        var regions = entries.GroupBy(entry => entry.Region, StringComparer.Ordinal).ToArray();
        if (regions.Length != 7)
        {
            throw new InvalidOperationException(
                $"Province catalog must contain exactly 7 regions; found {regions.Length}.");
        }
        var inconsistentRegion = regions.FirstOrDefault(
            region => region.Select(entry => entry.Color).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1);
        if (inconsistentRegion is not null)
        {
            throw new InvalidOperationException(
                $"Region '{inconsistentRegion.Key}' must use one consistent province color.");
        }

        return entries.AsReadOnly();
    }

    private static string ReadRequiredText(JsonElement properties, string name)
    {
        if (!properties.TryGetProperty(name, out var element))
            throw new InvalidOperationException($"Province property '{name}' is required.");

        var value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Province property '{name}' must not be empty.");
        return value.Trim();
    }

    private static double ReadRequiredDouble(JsonElement properties, string name)
    {
        if (!properties.TryGetProperty(name, out var element))
            throw new InvalidOperationException($"Province property '{name}' is required.");

        var parsed = element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(
                element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => double.NaN,
        };
        if (!double.IsFinite(parsed))
            throw new InvalidOperationException($"Province property '{name}' must be a finite number.");
        return parsed;
    }

    private static JsonSerializerOptions CreateGeoJsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new GeoJsonConverterFactory());
        return options;
    }
}
