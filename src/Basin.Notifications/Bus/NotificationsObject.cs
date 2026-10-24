using System.Text;
using Tmds.DBus.Protocol;
using static Basin.Notifications.NotificationsLog;

namespace Basin.Notifications;

internal sealed class NotificationsObject : IPathMethodHandler
{
    public const string ObjectPath = "/org/freedesktop/Notifications";
    public const string Interface = "org.freedesktop.Notifications";
    public const string SpecVersion = "1.2";

    private static readonly string[] Capabilities = ["actions", "body", "body-markup", "icon-static", "persistence", "sound"];

    private static readonly ReadOnlyMemory<byte> IntrospectXml = Encoding.UTF8.GetBytes(
        """
        <interface name="org.freedesktop.Notifications">
          <method name="Notify"><arg name="app_name" type="s" direction="in"/><arg name="replaces_id" type="u" direction="in"/><arg name="app_icon" type="s" direction="in"/><arg name="summary" type="s" direction="in"/><arg name="body" type="s" direction="in"/><arg name="actions" type="as" direction="in"/><arg name="hints" type="a{sv}" direction="in"/><arg name="expire_timeout" type="i" direction="in"/><arg name="id" type="u" direction="out"/></method>
          <method name="CloseNotification"><arg name="id" type="u" direction="in"/></method>
          <method name="GetCapabilities"><arg name="capabilities" type="as" direction="out"/></method>
          <method name="GetServerInformation"><arg name="name" type="s" direction="out"/><arg name="vendor" type="s" direction="out"/><arg name="version" type="s" direction="out"/><arg name="spec_version" type="s" direction="out"/></method>
          <signal name="NotificationClosed"><arg name="id" type="u"/><arg name="reason" type="u"/></signal>
          <signal name="ActionInvoked"><arg name="id" type="u"/><arg name="action_key" type="s"/></signal>
          <signal name="ActivationToken"><arg name="id" type="u"/><arg name="activation_token" type="s"/></signal>
        </interface>
        """);

    private readonly NotificationServer _server;
    private readonly NotificationServerOptions _options;

    public NotificationsObject(NotificationServer server, NotificationServerOptions options)
    {
        _server = server;
        _options = options;
    }

    public static IReadOnlyList<string> CapabilityNames => Capabilities;

    string IPathMethodHandler.Path => ObjectPath;

    bool IPathMethodHandler.HandlesChildPaths => false;

    ValueTask IPathMethodHandler.HandleMethodAsync(MethodContext context)
    {
        try
        {
            Handle(context);
        }
        catch (Exception error)
        {
            Log.Warn(error, $"{context.Request.MemberAsString} from {context.Request.SenderAsString} failed");
            TryReplyError(context, "org.freedesktop.DBus.Error.InvalidArgs", error.Message);
        }

        return default;
    }

    internal static void TryReplyError(MethodContext context, string name, string message)
    {
        try
        {
            if (!context.ReplySent && !context.NoReplyExpected)
            {
                context.ReplyError(name, message);
            }
        }
        catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException or DBusExceptionBase)
        {
            Log.Debug(error, $"bus: error reply not sent");
        }
    }

    private void Handle(MethodContext context)
    {
        var request = context.Request;
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([IntrospectXml], []);
            return;
        }

        if (request.InterfaceAsString != Interface)
        {
            context.ReplyUnknownMethodError();
            return;
        }

        var signature = request.SignatureAsString ?? "";
        switch (request.MemberAsString)
        {
            case "Notify" when signature == "susssasa{sv}i":
            {
                var notify = ReadNotify(request);
                _server.Answer(context, notify, static (server, context, notify) =>
                {
                    var id = server.Accept(notify.Notification, notify.ReplacesId);
                    if (!context.NoReplyExpected)
                    {
                        using var writer = context.CreateReplyWriter("u");
                        writer.WriteUInt32(id);
                        context.Reply(writer.CreateMessage());
                    }
                });
                return;
            }

            case "CloseNotification" when signature == "u":
            {
                var id = request.GetBodyReader().ReadUInt32();
                _server.Answer(context, id, static (server, context, id) =>
                {
                    server.Close(id, NotificationCloseReason.Closed);
                    ReplyEmpty(context);
                });
                return;
            }

            case "GetCapabilities" when signature.Length == 0:
            {
                if (!context.NoReplyExpected)
                {
                    using var writer = context.CreateReplyWriter("as");
                    var array = writer.WriteArrayStart(DBusType.String);
                    foreach (var capability in Capabilities)
                    {
                        writer.WriteString(capability);
                    }

                    writer.WriteArrayEnd(array);
                    context.Reply(writer.CreateMessage());
                }

                return;
            }

            case "GetServerInformation" when signature.Length == 0:
            {
                if (!context.NoReplyExpected)
                {
                    using var writer = context.CreateReplyWriter("ssss");
                    writer.WriteString(_options.ServerName);
                    writer.WriteString(_options.Vendor);
                    writer.WriteString(_options.Version);
                    writer.WriteString(SpecVersion);
                    context.Reply(writer.CreateMessage());
                }

                return;
            }

            default:
                context.ReplyUnknownMethodError();
                return;
        }
    }

    private static NotifyRequest ReadNotify(Message request)
    {
        var reader = request.GetBodyReader();
        var appName = reader.ReadString();
        var replacesId = reader.ReadUInt32();
        var appIcon = reader.ReadString();
        var summary = reader.ReadString();
        var markup = reader.ReadString();
        var actionStrings = reader.ReadArrayOfString();
        var body = NotificationMarkup.Reduce(markup, out var spans);
        var actions = new NotificationAction[actionStrings.Length / 2];
        for (var i = 0; i < actions.Length; i++)
        {
            actions[i] = new NotificationAction(actionStrings[2 * i], actionStrings[(2 * i) + 1]);
        }

        var notification = new Notification
        {
            AppName = appName,
            AppIcon = appIcon,
            Summary = summary,
            Body = body,
            BodySpans = spans,
            Actions = actions,
            Sender = request.SenderAsString,
        };
        notification = NotificationHints.Read(ref reader, notification);
        var expire = reader.ReadInt32();
        return new NotifyRequest(notification with { ExpireTimeout = expire }, replacesId);
    }

    private static void ReplyEmpty(MethodContext context)
    {
        if (context.NoReplyExpected)
        {
            return;
        }

        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
    }
}
