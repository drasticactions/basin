using System.Text.Json.Serialization;

namespace TabWm;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(LauncherFile))]
[JsonSerializable(typeof(SessionFile))]
internal sealed partial class TabWmJsonContext : JsonSerializerContext
{
}
