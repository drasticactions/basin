using System.Text;
using Tmds.DBus.Protocol;
using static Basin.Accessibility.AccessibilityLog;

namespace Basin.Accessibility;

internal sealed class A11yManagerObject : IPathMethodHandler
{
    public const string ObjectPath = "/org/freedesktop/a11y/Manager";
    public const string KeyboardInterface = "org.freedesktop.a11y.KeyboardMonitor";
    public const string PointerInterface = "org.freedesktop.a11y.PointerLocator";

    private static readonly ReadOnlyMemory<byte> KeyboardXml = Encoding.UTF8.GetBytes(
        """
        <interface name="org.freedesktop.a11y.KeyboardMonitor">
          <method name="GrabKeyboard"/>
          <method name="UngrabKeyboard"/>
          <method name="WatchKeyboard"/>
          <method name="UnwatchKeyboard"/>
          <method name="SetKeyGrabs"><arg name="modifiers" type="au" direction="in"/><arg name="keystrokes" type="a(uu)" direction="in"/></method>
          <signal name="KeyEvent"><arg name="released" type="b"/><arg name="state" type="u"/><arg name="keysym" type="u"/><arg name="unichar" type="u"/><arg name="keycode" type="q"/></signal>
        </interface>
        """);

    private static readonly ReadOnlyMemory<byte> PointerXml = Encoding.UTF8.GetBytes(
        """
        <interface name="org.freedesktop.a11y.PointerLocator">
          <method name="QueryPointer"><arg name="data" type="a{sv}" direction="out"/><arg name="x" type="d" direction="out"/><arg name="y" type="d" direction="out"/></method>
          <signal name="PointerPositionChanged"/>
        </interface>
        """);

    private readonly A11yManager _manager;

    public A11yManagerObject(A11yManager manager) => _manager = manager;

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

    internal static void ReplyEmpty(MethodContext context)
    {
        if (!context.NoReplyExpected)
        {
            using var writer = context.CreateReplyWriter(null);
            context.Reply(writer.CreateMessage());
        }
    }

    internal static void ReplyPointer(MethodContext context, in PointerAnswer answer)
    {
        if (context.NoReplyExpected)
        {
            return;
        }

        using var writer = context.CreateReplyWriter("a{sv}dd");
        var map = writer.WriteDictionaryStart();
        if (answer.Pid > 0)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString("pid");
            writer.WriteVariantInt32(answer.Pid);
        }

        if (answer.AppBusName is { } bus && answer.ToplevelPath is { } path)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString("app-dbus-name");
            writer.WriteVariantString(bus);
            writer.WriteDictionaryEntryStart();
            writer.WriteString("toplevel-object-path");
            writer.WriteVariantObjectPath(path);
        }

        writer.WriteDictionaryEnd(map);
        writer.WriteDouble(answer.X);
        writer.WriteDouble(answer.Y);
        context.Reply(writer.CreateMessage());
    }

    private void Handle(MethodContext context)
    {
        var request = context.Request;
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([KeyboardXml, PointerXml], []);
            return;
        }

        var sender = request.SenderAsString ?? "";
        var signature = request.SignatureAsString ?? "";
        switch (request.InterfaceAsString, request.MemberAsString)
        {
            case (KeyboardInterface, "GrabKeyboard"):
                _manager.Answer(context, sender, static (state, name) => state.Client(name).Grabbed = true);
                return;
            case (KeyboardInterface, "UngrabKeyboard"):
                _manager.Answer(context, sender, static (state, name) => state.Client(name).Grabbed = false);
                return;
            case (KeyboardInterface, "WatchKeyboard"):
                _manager.Answer(context, sender, static (state, name) => state.Client(name).Watched = true);
                return;
            case (KeyboardInterface, "UnwatchKeyboard"):
                _manager.Answer(context, sender, static (state, name) => state.Client(name).Watched = false);
                return;
            case (KeyboardInterface, "SetKeyGrabs") when signature == "aua(uu)":
            {
                var reader = request.GetBodyReader();
                var modifiers = reader.ReadArrayOfUInt32();
                var keystrokes = new List<(uint, uint)>();
                var array = reader.ReadArrayStart(DBusType.Struct);
                while (reader.HasNext(array))
                {
                    reader.AlignStruct();
                    keystrokes.Add((reader.ReadUInt32(), reader.ReadUInt32()));
                }

                _manager.Answer(context, sender, (state, name) => state.SetKeyGrabs(name, modifiers, keystrokes));
                return;
            }

            case (PointerInterface, "QueryPointer"):
                _manager.QueryPointer(context, sender);
                return;
            default:
                context.ReplyUnknownMethodError();
                return;
        }
    }
}
