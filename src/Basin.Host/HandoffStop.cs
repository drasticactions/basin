namespace Basin.Host;

/// <summary>
/// Identifies why a compositor stops. <see cref="HandoffPolicy.RetainsOn"/> uses it to decide whether the last frame stays lit.
/// </summary>
public enum HandoffStop
{
    /// <summary>
    /// An ordinary exit, for example a quit command, a signal, a logout or the end of <c>--frames</c>.
    /// </summary>
    Orderly,
    /// <summary>
    /// A greeter exits so that greetd can start the session.
    /// </summary>
    SessionStart,
    /// <summary>
    /// The compositor exits because of an error. The last frame never stays lit.
    /// </summary>
    Failure,
}
