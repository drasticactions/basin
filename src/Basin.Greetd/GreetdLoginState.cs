namespace Basin.Greetd;

/// <summary>
/// The state of a <see cref="GreetdLogin"/>.
/// </summary>
public enum GreetdLoginState
{
    /// <summary>No session is open. <see cref="GreetdLogin.Start"/> can begin one.</summary>
    Idle,
    /// <summary>A request is outstanding. The login accepts no call until greetd answers.</summary>
    Waiting,
    /// <summary>greetd sent an auth message. The greeter must answer it or acknowledge it.</summary>
    Prompting,
    /// <summary>Authentication is complete. <see cref="GreetdLogin.StartSession"/> can start the session.</summary>
    Ready,
    /// <summary>The <c>start_session</c> request is outstanding.</summary>
    Starting,
    /// <summary>greetd accepted the session. The greeter must exit so that the session can start.</summary>
    Started,
    /// <summary>greetd sent an error, or the connection closed. <see cref="GreetdLogin.Start"/> can begin again when the connection is open.</summary>
    Failed,
}
