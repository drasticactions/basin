using System.Text.Json.Serialization;

namespace Basin.Greetd;

/// <summary>
/// The <c>create_session</c> request. It opens a PAM conversation for one user. greetd refuses it while another session is open.
/// </summary>
/// <param name="Username">The login name of the user to authenticate.</param>
public sealed record GreetdCreateSession([property: JsonPropertyName("username")] string Username) : GreetdRequest;
