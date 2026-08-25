using Avalonia.Threading;
using Basin.Diagnostics;
using Tmds.DBus.Protocol;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class PortalGlobalHotkeys : IDisposable
{
    private const string Portal = "org.freedesktop.portal.Desktop";

    private const string PortalPath = "/org/freedesktop/portal/desktop";

    private const string ShortcutsInterface = "org.freedesktop.portal.GlobalShortcuts";

    private readonly IReadOnlyList<Hotkey> _hotkeys;

    private readonly Action<Hotkey> _launch;

    private readonly Dictionary<string, TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)>> _pending = [];

    private DBusConnection? _connection;

    private volatile bool _disposed;

    private PortalGlobalHotkeys(IReadOnlyList<Hotkey> hotkeys, Action<Hotkey> launch)
    {
        _hotkeys = hotkeys;
        _launch = launch;
    }

    public static PortalGlobalHotkeys Start(IReadOnlyList<Hotkey> hotkeys, Action<Hotkey> launch)
    {
        var instance = new PortalGlobalHotkeys(hotkeys, launch);
        _ = instance.BindAsync();
        return instance;
    }

    public void Dispose()
    {
        _disposed = true;
        _connection?.Dispose();
        _connection = null;
    }

    private async Task BindAsync()
    {
        try
        {
            if (DBusAddress.Session is not { } address)
            {
                Log.Warn($"this session has no D-Bus session bus, global hotkeys are off");
                return;
            }

            var connection = new DBusConnection(address);
            _connection = connection;
            await connection.ConnectAsync();
            await RegisterAppIdAsync(connection);
            var sender = (connection.UniqueName ?? string.Empty).TrimStart(':').Replace('.', '_');
            _ = await connection.AddMatchAsync(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Sender = Portal,
                    Interface = "org.freedesktop.portal.Request",
                    Member = "Response",
                },
                ReadResponse,
                OnResponse,
                emitOnCapturedContext: false,
                ObserverFlags.None,
                state: this);

            var sessionResponse = await CreateSessionAsync(connection, sender);
            if (sessionResponse.Code != 0)
            {
                Log.Warn($"the desktop refused a global shortcuts session (response {sessionResponse.Code}), global hotkeys are off");
                return;
            }

            if (!sessionResponse.Results.TryGetValue("session_handle", out var handle))
            {
                Log.Warn($"the portal reply carried no session handle, global hotkeys are off");
                return;
            }

            var session = handle.Type == VariantValueType.ObjectPath ? handle.GetObjectPathAsString() : handle.GetString();
            _ = await connection.AddMatchAsync(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Sender = Portal,
                    Interface = ShortcutsInterface,
                    Member = "Activated",
                    Path = PortalPath,
                },
                ReadActivation,
                OnActivation,
                emitOnCapturedContext: false,
                ObserverFlags.None,
                state: (this, session));

            var bindResponse = await BindShortcutsAsync(connection, sender, session);
            if (bindResponse.Code != 0)
            {
                Log.Warn($"the desktop refused the shortcut list (response {bindResponse.Code}), global hotkeys are off");
                return;
            }

            Log.Debug($"{_hotkeys.Count} global hotkey(s) bound through the desktop portal");
        }
        catch (DBusErrorReplyException error) when (error.ErrorName.Contains("ServiceUnknown"))
        {
            Log.Warn($"xdg-desktop-portal is not running, global hotkeys are off");
        }
        catch (DBusErrorReplyException error) when (
            error.ErrorName.Contains("UnknownInterface") || error.ErrorName.Contains("UnknownMethod"))
        {
            Log.Warn($"this desktop has no GlobalShortcuts portal backend, global hotkeys are off");
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            if (!_disposed)
            {
                Log.Warn($"the global shortcuts portal failed: {error.Message}");
            }
        }
    }

    private static async Task RegisterAppIdAsync(DBusConnection connection)
    {
        try
        {
            await connection.CallMethodAsync(RegisterMessage(connection));
        }
        catch (DBusErrorReplyException error)
        {
            Log.Warn($"no app id was registered ({error.ErrorMessage}), the desktop may refuse global shortcuts");
        }
    }

    private static MessageBuffer RegisterMessage(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            Portal, PortalPath, "org.freedesktop.host.portal.Registry", "Register", "sa{sv}");
        writer.WriteString("waylonia");
        var options = writer.WriteDictionaryStart();
        writer.WriteDictionaryEnd(options);
        return writer.CreateMessage();
    }

    private Task<(uint Code, Dictionary<string, VariantValue> Results)> CreateSessionAsync(
        DBusConnection connection, string sender)
    {
        var pending = Expect(sender, "waylonia_create");
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Portal, PortalPath, ShortcutsInterface, "CreateSession", "a{sv}");
        var options = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("handle_token");
        writer.WriteVariantString("waylonia_create");
        writer.WriteDictionaryEntryStart();
        writer.WriteString("session_handle_token");
        writer.WriteVariantString("waylonia");
        writer.WriteDictionaryEnd(options);
        return SendAsync(connection, writer.CreateMessage(), pending);
    }

    private Task<(uint Code, Dictionary<string, VariantValue> Results)> BindShortcutsAsync(
        DBusConnection connection, string sender, string session)
    {
        var pending = Expect(sender, "waylonia_bind");
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Portal, PortalPath, ShortcutsInterface, "BindShortcuts", "oa(sa{sv})sa{sv}");
        writer.WriteObjectPath(session);
        var shortcuts = writer.WriteArrayStart(DBusType.Struct);
        foreach (var hotkey in _hotkeys)
        {
            writer.WriteStructureStart();
            writer.WriteString(hotkey.Chord);
            var entry = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("description");
            writer.WriteVariantString($"Launch {hotkey.Command}");
            writer.WriteDictionaryEntryStart();
            writer.WriteString("preferred_trigger");
            writer.WriteVariantString(Trigger(hotkey));
            writer.WriteDictionaryEnd(entry);
        }

        writer.WriteArrayEnd(shortcuts);
        writer.WriteString(string.Empty);
        var options = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("handle_token");
        writer.WriteVariantString("waylonia_bind");
        writer.WriteDictionaryEnd(options);
        return SendAsync(connection, writer.CreateMessage(), pending);
    }

    private Task<(uint Code, Dictionary<string, VariantValue> Results)> Expect(string sender, string token)
    {
        var source = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending)
        {
            _pending[$"/org/freedesktop/portal/desktop/request/{sender}/{token}"] = source;
        }

        return source.Task;
    }

    private static async Task<(uint Code, Dictionary<string, VariantValue> Results)> SendAsync(
        DBusConnection connection,
        MessageBuffer message,
        Task<(uint Code, Dictionary<string, VariantValue> Results)> pending)
    {
        await connection.CallMethodAsync(message);
        return await pending;
    }

    private static (string Path, uint Code, Dictionary<string, VariantValue> Results) ReadResponse(
        Message message, object? state)
    {
        var reader = message.GetBodyReader();
        var code = reader.ReadUInt32();
        var results = reader.ReadDictionaryOfStringToVariantValue();
        return (message.PathAsString ?? string.Empty, code, results);
    }

    private static void OnResponse(
        Notification<(string Path, uint Code, Dictionary<string, VariantValue> Results)> notification)
    {
        if (!notification.HasValue || notification.State is not PortalGlobalHotkeys instance)
        {
            return;
        }

        var response = notification.Value;
        TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>? pending;
        lock (instance._pending)
        {
            if (instance._pending.Remove(response.Path, out pending) is false)
            {
                return;
            }
        }

        pending?.TrySetResult((response.Code, response.Results));
    }

    private static (string Session, string Id) ReadActivation(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        var session = reader.ReadObjectPathAsString();
        var id = reader.ReadString();
        return (session, id);
    }

    private static void OnActivation(Notification<(string Session, string Id)> notification)
    {
        if (!notification.HasValue || notification.State is not (PortalGlobalHotkeys instance, string session))
        {
            return;
        }

        var activation = notification.Value;
        if (instance._disposed || activation.Session != session)
        {
            return;
        }

        foreach (var hotkey in instance._hotkeys)
        {
            if (hotkey.Chord == activation.Id)
            {
                var captured = hotkey;
                Dispatcher.UIThread.Post(() => instance._launch(captured));
                return;
            }
        }
    }

    private static string Trigger(Hotkey hotkey)
    {
        var parts = new List<string>(5);
        if ((hotkey.Modifiers & HotkeyModifiers.Ctrl) != 0)
        {
            parts.Add("CTRL");
        }

        if ((hotkey.Modifiers & HotkeyModifiers.Shift) != 0)
        {
            parts.Add("SHIFT");
        }

        if ((hotkey.Modifiers & HotkeyModifiers.Alt) != 0)
        {
            parts.Add("ALT");
        }

        if ((hotkey.Modifiers & HotkeyModifiers.Super) != 0)
        {
            parts.Add("LOGO");
        }

        parts.Add(KeyName(hotkey.Key));
        return string.Join('+', parts);
    }

    private static string KeyName(string key) => key switch
    {
        "f1" => "F1", "f2" => "F2", "f3" => "F3", "f4" => "F4", "f5" => "F5", "f6" => "F6",
        "f7" => "F7", "f8" => "F8", "f9" => "F9", "f10" => "F10", "f11" => "F11", "f12" => "F12",
        "enter" or "return" => "Return", "tab" => "Tab", "escape" => "Escape",
        "backspace" => "BackSpace", "delete" => "Delete", "insert" => "Insert",
        "home" => "Home", "end" => "End", "pageup" => "Page_Up", "pagedown" => "Page_Down",
        "left" => "Left", "up" => "Up", "right" => "Right", "down" => "Down",
        _ => key,
    };
}
