using System.Text.Json.Serialization;

namespace Basin.Greetd;

/// <summary>
/// A response from greetd. greetd sends exactly one response for each request and sends no events.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GreetdSuccess), "success")]
[JsonDerivedType(typeof(GreetdError), "error")]
[JsonDerivedType(typeof(GreetdAuthMessage), "auth_message")]
public abstract record GreetdResponse;
