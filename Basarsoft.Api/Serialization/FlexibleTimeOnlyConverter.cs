using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basarsoft.Api.Serialization;

// System.Text.Json's built-in TimeOnly converter only accepts "HH:mm:ss[.fffffff]", but an HTML
// <input type="time"> submits "HH:mm". This converter accepts both on read (TimeOnly.Parse handles
// either) and always writes "HH:mm:ss", keeping responses identical to the default format.
public class FlexibleTimeOnlyConverter : JsonConverter<TimeOnly>
{
    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (!TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new JsonException($"'{text}' is not a valid time. Expected HH:mm or HH:mm:ss.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
}
