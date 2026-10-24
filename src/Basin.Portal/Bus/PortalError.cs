using Tmds.DBus.Protocol;

namespace Basin.Portal;

/// <summary>
/// The D-Bus error names that the backend replies with, and factories for the matching reply exceptions.
/// </summary>
public static class PortalError
{
    /// <summary>
    /// The error for a caller that is not the portal frontend.
    /// </summary>
    public const string AccessDenied = "org.freedesktop.DBus.Error.AccessDenied";

    /// <summary>
    /// The error for a call to a portal interface whose module is not installed.
    /// </summary>
    public const string UnknownInterface = "org.freedesktop.DBus.Error.UnknownInterface";

    /// <summary>
    /// The error for a call to a request or session path that does not exist.
    /// </summary>
    public const string UnknownObject = "org.freedesktop.DBus.Error.UnknownObject";

    /// <summary>
    /// The portal error for an argument or option with a wrong type or value.
    /// </summary>
    public const string InvalidArgument = "org.freedesktop.portal.Error.InvalidArgument";

    /// <summary>
    /// The portal error for an operation that the session state or the policy does not permit.
    /// </summary>
    public const string NotAllowed = "org.freedesktop.portal.Error.NotAllowed";

    /// <summary>
    /// The portal error for a feature that the compositor cannot supply.
    /// </summary>
    public const string NotSupported = "org.freedesktop.portal.Error.NotSupported";

    /// <summary>
    /// The portal error for a session, source or item that does not exist.
    /// </summary>
    public const string NotFound = "org.freedesktop.portal.Error.NotFound";

    /// <summary>
    /// Creates an <see cref="InvalidArgument"/> reply.
    /// </summary>
    /// <param name="message">The text for the caller.</param>
    /// <returns>The exception that the handler turns into the error reply.</returns>
    public static DBusErrorReplyException Invalid(string message) => new(InvalidArgument, message);

    /// <summary>
    /// Creates a <see cref="NotAllowed"/> reply.
    /// </summary>
    /// <param name="message">The text for the caller.</param>
    /// <returns>The exception that the handler turns into the error reply.</returns>
    public static DBusErrorReplyException Denied(string message) => new(NotAllowed, message);

    /// <summary>
    /// Creates a <see cref="NotSupported"/> reply.
    /// </summary>
    /// <param name="message">The text for the caller.</param>
    /// <returns>The exception that the handler turns into the error reply.</returns>
    public static DBusErrorReplyException Unsupported(string message) => new(NotSupported, message);

    /// <summary>
    /// Creates a <see cref="NotFound"/> reply.
    /// </summary>
    /// <param name="message">The text for the caller.</param>
    /// <returns>The exception that the handler turns into the error reply.</returns>
    public static DBusErrorReplyException Missing(string message) => new(NotFound, message);

    /// <summary>
    /// Creates an <see cref="UnknownInterface"/> reply that says the interface is not installed.
    /// </summary>
    /// <param name="name">The D-Bus interface name.</param>
    /// <returns>The exception that the handler turns into the error reply.</returns>
    public static DBusErrorReplyException NoInterface(string name) =>
        new(UnknownInterface, $"{name} is not installed on this compositor");

    /// <summary>
    /// Creates an <see cref="UnknownObject"/> reply that names the path.
    /// </summary>
    /// <param name="path">The object path that does not exist.</param>
    /// <returns>The exception that the handler turns into the error reply.</returns>
    public static DBusErrorReplyException NoObject(string path) => new(UnknownObject, $"no object at {path}");
}
