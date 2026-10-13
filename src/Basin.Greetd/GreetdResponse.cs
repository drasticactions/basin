using System.Text.Json.Serialization;

namespace Basin.Greetd;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GreetdSuccess), "success")]
[JsonDerivedType(typeof(GreetdError), "error")]
[JsonDerivedType(typeof(GreetdAuthMessage), "auth_message")]
public abstract record GreetdResponse;
