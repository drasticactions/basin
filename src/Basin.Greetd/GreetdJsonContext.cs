using System.Text.Json.Serialization;

namespace Basin.Greetd;

[JsonSourceGenerationOptions(AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(GreetdRequest))]
[JsonSerializable(typeof(GreetdResponse))]
public sealed partial class GreetdJsonContext : JsonSerializerContext;
