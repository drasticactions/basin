using Tmds.DBus.Protocol;

namespace Basin.Portal;

public static class PortalError
{
    public const string AccessDenied = "org.freedesktop.DBus.Error.AccessDenied";

    public const string UnknownInterface = "org.freedesktop.DBus.Error.UnknownInterface";

    public const string UnknownObject = "org.freedesktop.DBus.Error.UnknownObject";

    public const string InvalidArgument = "org.freedesktop.portal.Error.InvalidArgument";

    public const string NotAllowed = "org.freedesktop.portal.Error.NotAllowed";

    public const string NotSupported = "org.freedesktop.portal.Error.NotSupported";

    public const string NotFound = "org.freedesktop.portal.Error.NotFound";

    public static DBusErrorReplyException Invalid(string message) => new(InvalidArgument, message);

    public static DBusErrorReplyException Denied(string message) => new(NotAllowed, message);

    public static DBusErrorReplyException Unsupported(string message) => new(NotSupported, message);

    public static DBusErrorReplyException Missing(string message) => new(NotFound, message);

    public static DBusErrorReplyException NoInterface(string name) =>
        new(UnknownInterface, $"{name} is not installed on this compositor");

    public static DBusErrorReplyException NoObject(string path) => new(UnknownObject, $"no object at {path}");
}
