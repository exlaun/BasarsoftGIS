using System.Text.Json;
using Basarsoft.Api.Serialization;
using Xunit;

namespace Basarsoft.Api.Tests;

// The POI working-hours converter must accept both what <input type="time"> submits ("HH:mm") and
// the canonical "HH:mm:ss" — and turn every malformed value into JsonException, which MVC maps to
// a 400. (A non-string token used to escape as InvalidOperationException -> 500.)
public class FlexibleTimeOnlyConverterTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new FlexibleTimeOnlyConverter());
        return options;
    }

    [Theory]
    [InlineData("\"09:30\"", 9, 30, 0)]
    [InlineData("\"09:30:15\"", 9, 30, 15)]
    [InlineData("\"00:00\"", 0, 0, 0)]
    public void Read_AcceptsHtmlTimeInputAndCanonicalFormats(string json, int hour, int minute, int second)
    {
        var value = JsonSerializer.Deserialize<TimeOnly>(json, Options);
        Assert.Equal(new TimeOnly(hour, minute, second), value);
    }

    [Theory]
    [InlineData("900")]          // number token, e.g. {"openTime": 900}
    [InlineData("true")]         // wrong token type
    [InlineData("null")]         // TimeOnly is non-nullable here
    [InlineData("\"25:00\"")]    // out of range
    [InlineData("\"lunch\"")]    // not a time at all
    public void Read_MalformedValue_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TimeOnly>(json, Options));
    }

    [Fact]
    public void Write_AlwaysEmitsCanonicalSecondsFormat()
    {
        var json = JsonSerializer.Serialize(new TimeOnly(9, 30), Options);
        Assert.Equal("\"09:30:00\"", json);
    }
}
