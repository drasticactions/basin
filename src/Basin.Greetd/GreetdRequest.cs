using System.Text.Json.Serialization;

namespace Basin.Greetd;

/// <summary>
/// A request from the greeter to greetd. The <c>type</c> field selects the subtype.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GreetdCreateSession), "create_session")]
[JsonDerivedType(typeof(GreetdPostAuthMessageResponse), "post_auth_message_response")]
[JsonDerivedType(typeof(GreetdStartSession), "start_session")]
[JsonDerivedType(typeof(GreetdCancelSession), "cancel_session")]
public abstract record GreetdRequest;
