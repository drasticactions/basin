namespace Basin.Ipc;

/// <summary>
/// The error codes that the control socket writes in the <c>code</c> of an error reply.
/// </summary>
public static class IpcErrorCodes
{
    /// <summary>
    /// The frame is not JSON, or the request envelope is malformed.
    /// </summary>
    public const string ParseError = "parse_error";

    /// <summary>
    /// The compositor has no method with the requested name. An omitted method and a method whose capability is absent also give this code.
    /// </summary>
    public const string UnknownMethod = "unknown_method";

    /// <summary>
    /// A parameter is missing, has the wrong type or is out of range.
    /// </summary>
    public const string InvalidParams = "invalid_params";

    /// <summary>
    /// The capability behind the method is not in this session, or the front delivers no events.
    /// </summary>
    public const string Unavailable = "unavailable";

    /// <summary>
    /// The window, output, workspace or other named object does not exist.
    /// </summary>
    public const string NotFound = "not_found";

    /// <summary>
    /// The capability answered false, or compositor policy or an interceptor declined the call.
    /// </summary>
    public const string Refused = "refused";

    /// <summary>
    /// The reply does not fit in one frame of <see cref="IpcProtocol.MaxMessageBytes"/>.
    /// </summary>
    public const string TooLarge = "too_large";

    /// <summary>
    /// The mechanism ran and failed, or a wait timed out. The message says why.
    /// </summary>
    public const string Failed = "failed";

    /// <summary>
    /// A handler threw an exception. The server logs it at error level.
    /// </summary>
    public const string Internal = "internal";
}
