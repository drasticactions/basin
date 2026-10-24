using System.Text;
using Tmds.DBus.Protocol;
using static Basin.SoftKeyboard.Fcitx5.Fcitx5Log;

namespace Basin.SoftKeyboard.Fcitx5;

internal sealed class PanelObject : IPathMethodHandler
{
    public const string ObjectPath = "/org/fcitx/virtualkeyboard/impanel";
    public const string Interface = "org.fcitx.Fcitx5.VirtualKeyboard1";

    private static readonly ReadOnlyMemory<byte> IntrospectXml = Encoding.UTF8.GetBytes(
        """
        <interface name="org.fcitx.Fcitx5.VirtualKeyboard1">
          <method name="ShowVirtualKeyboard"/>
          <method name="HideVirtualKeyboard"/>
          <method name="UpdatePreeditCaret"><arg name="caret" type="i" direction="in"/></method>
          <method name="UpdatePreeditArea"><arg name="preedit" type="s" direction="in"/></method>
          <method name="UpdateCandidateArea"><arg name="candidates" type="as" direction="in"/><arg name="has_prev" type="b" direction="in"/><arg name="has_next" type="b" direction="in"/><arg name="page" type="i" direction="in"/><arg name="cursor" type="i" direction="in"/></method>
          <method name="NotifyIMActivated"><arg name="name" type="s" direction="in"/></method>
          <method name="NotifyIMDeactivated"><arg name="name" type="s" direction="in"/></method>
          <method name="NotifyIMListChanged"/>
        </interface>
        """);

    private readonly Fcitx5Keyboard _owner;

    public PanelObject(Fcitx5Keyboard owner)
    {
        _owner = owner;
    }

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
            Log.Error(error, $"fcitx5: panel method {context.Request.MemberAsString} failed");
            TryReplyError(context, error.Message);
        }

        return default;
    }

    private void Handle(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([IntrospectXml], []);
            return;
        }

        var request = context.Request;
        if (request.InterfaceAsString != Interface)
        {
            TryReplyError(context, $"unknown interface {request.InterfaceAsString}");
            return;
        }

        var reader = request.GetBodyReader();
        switch (request.MemberAsString)
        {
            case "ShowVirtualKeyboard":
                _owner.Post(static owner => owner.OnShowRequested(true));
                break;
            case "HideVirtualKeyboard":
                _owner.Post(static owner => owner.OnShowRequested(false));
                break;
            case "UpdatePreeditCaret":
                var caret = reader.ReadInt32();
                _owner.Post(owner => owner.OnPreeditCaret(caret));
                break;
            case "UpdatePreeditArea":
                var preedit = reader.ReadString();
                _owner.Post(owner => owner.OnPreedit(preedit));
                break;
            case "UpdateCandidateArea":
                var words = reader.ReadArrayOfString();
                var hasPrevious = reader.ReadBool();
                var hasNext = reader.ReadBool();
                var page = reader.ReadInt32();
                var cursor = reader.ReadInt32();
                var candidates = new Fcitx5Candidates(words, hasPrevious, hasNext, page, cursor);
                _owner.Post(owner => owner.OnCandidates(candidates));
                break;
            case "NotifyIMActivated":
                var activated = reader.ReadString();
                _owner.Post(owner => owner.OnInputMethod(activated));
                break;
            case "NotifyIMDeactivated":
                var deactivated = reader.ReadString();
                _owner.Post(owner => owner.OnInputMethodGone(deactivated));
                break;
            case "NotifyIMListChanged":
                break;
            default:
                TryReplyError(context, $"unknown method {request.MemberAsString}");
                return;
        }

        if (!context.NoReplyExpected)
        {
            using var writer = context.CreateReplyWriter(null);
            context.Reply(writer.CreateMessage());
        }
    }

    private static void TryReplyError(MethodContext context, string message)
    {
        try
        {
            if (!context.ReplySent && !context.NoReplyExpected)
            {
                context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", message);
            }
        }
        catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException or DBusExceptionBase)
        {
            Log.Debug(error, $"fcitx5: the error reply was not sent");
        }
    }
}
