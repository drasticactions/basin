using System.Text.Json.Serialization;

namespace Basin.SoftKeyboard;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SoftKeyboardStateFile))]
internal sealed partial class SoftKeyboardJsonContext : JsonSerializerContext
{
}
