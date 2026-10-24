using System.Text.Json.Serialization;

namespace Basin.Greetd;

/// <summary>
/// The source-generated JSON context for <see cref="GreetdRequest"/> and <see cref="GreetdResponse"/>. It reads the <c>type</c> field in any position.
/// </summary>
[JsonSourceGenerationOptions(AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(GreetdRequest))]
[JsonSerializable(typeof(GreetdResponse))]
public sealed partial class GreetdJsonContext : JsonSerializerContext;
