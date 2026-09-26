using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basin.Ipc;

[JsonConverter(typeof(IpcRawJsonConverter))]
public readonly record struct IpcRawJson(ReadOnlyMemory<byte> Utf8)
{
    [JsonIgnore]
    public bool IsEmpty => Utf8.IsEmpty;

    public JsonElement ToElement()
    {
        var reader = new Utf8JsonReader(Utf8.Span);
        return JsonElement.ParseValue(ref reader);
    }

    public override string ToString() => Encoding.UTF8.GetString(Utf8.Span);
}
