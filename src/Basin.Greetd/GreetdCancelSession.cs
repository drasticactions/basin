namespace Basin.Greetd;

/// <summary>
/// The <c>cancel_session</c> request. It ends the session that <see cref="GreetdCreateSession"/> opened, so that a new one can start.
/// </summary>
public sealed record GreetdCancelSession : GreetdRequest;
