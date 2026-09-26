using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace BasinMcp;

internal static class McpJson
{
    public static JsonSerializerOptions Options { get; } = new(McpJsonContext.Default.Options)
    {
        TypeInfoResolver = JsonTypeInfoResolver.Combine(McpJsonContext.Default, McpJsonUtilities.DefaultOptions.TypeInfoResolver!),
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Info<T>());

    public static T? Deserialize<T>(JsonElement element) => element.Deserialize(Info<T>());

    public static JsonElement Parse(string json) => Parse(Encoding.UTF8.GetBytes(json));

    public static JsonElement Parse(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);
        return JsonElement.ParseValue(ref reader);
    }

    public static JsonElement Object(IDictionary<string, JsonElement>? arguments) =>
        JsonSerializer.SerializeToElement(
            arguments as Dictionary<string, JsonElement> ?? new Dictionary<string, JsonElement>(arguments ?? new Dictionary<string, JsonElement>()),
            Info<Dictionary<string, JsonElement>>());

    public static CallToolResult TextResult(byte[] json, bool isError = false) => new()
    {
        Content = [new TextContentBlock { Text = Encoding.UTF8.GetString(json) }],
        StructuredContent = Parse(json),
        IsError = isError ? true : null,
    };

    public static CallToolResult Error(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        IsError = true,
    };
}
