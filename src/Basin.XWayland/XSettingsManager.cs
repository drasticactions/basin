using System.Buffers.Binary;
using System.Text;
using Xcb.Native;
using static Basin.XWayland.XWaylandLog;

namespace Basin.XWayland;

/// <summary>
/// Owns the <c>_XSETTINGS_S0</c> selection on an X server and publishes the XSETTINGS property for toolkits such as GTK. It publishes nothing when another client already owns the selection.
/// </summary>
public sealed unsafe class XSettingsManager
{
    private const byte EventClientMessage = 33;

    private readonly xcb_connection_t* _conn;
    private readonly ICompositorEventLoop _loop;
    private readonly uint _root;
    private readonly uint _window;
    private readonly uint _selection;
    private readonly uint _settingsAtom;
    private readonly uint _managerAtom;
    private readonly SortedDictionary<string, Setting> _settings = new(StringComparer.Ordinal);
    private uint _serial;
    private readonly Action? _flushed;
    private bool _flushQueued;

    internal XSettingsManager(
        xcb_connection_t* conn, ICompositorEventLoop loop, uint root, uint visual, Func<string, uint> atom, Action? flushed = null)
    {
        _flushed = flushed;
        _conn = conn;
        _loop = loop;
        _root = root;
        _selection = atom("_XSETTINGS_S0");
        _settingsAtom = atom("_XSETTINGS_SETTINGS");
        _managerAtom = atom("MANAGER");

        var cookie = Libxcb.xcb_get_selection_owner(_conn, _selection);
        var reply = Libxcb.xcb_get_selection_owner_reply(_conn, cookie, null);
        var owner = reply != null ? reply->owner : 0;
        if (reply != null)
        {
            Libc.Free(reply);
        }

        if (owner != 0)
        {
            ExistingOwner = owner;
            Log.Info($"_XSETTINGS_S0 is owned by window 0x{owner:x}, so basin publishes no XSETTINGS");
            return;
        }

        _window = Libxcb.xcb_generate_id(_conn);
        _ = Libxcb.xcb_create_window(
            _conn, 0, _window, _root, -1, -1, 1, 1, 0,
            (ushort)xcb_window_class_t.XCB_WINDOW_CLASS_INPUT_OUTPUT, visual, 0, null);
        _ = Libxcb.xcb_set_selection_owner(_conn, _window, _selection, 0);
        IsOwner = true;

        var message = new xcb_client_message_event_t
        {
            response_type = EventClientMessage,
            format = 32,
            window = _root,
            type = _managerAtom,
        };
        var data = (uint*)&message.data;
        data[0] = 0;
        data[1] = _selection;
        data[2] = _window;
        _ = Libxcb.xcb_send_event(_conn, 0, _root, 0x00020000, (sbyte*)&message);
    }

    /// <summary>
    /// Gets a value indicating whether this manager took the selection. When it is <see langword="false"/>, a setting change is stored but never published.
    /// </summary>
    public bool IsOwner { get; }

    /// <summary>
    /// Gets the X window that owned the selection before this manager started, or 0 when the selection was free.
    /// </summary>
    public uint ExistingOwner { get; }

    /// <summary>
    /// Gets the serial of the last change. Each stored change adds one. A removal does not change it.
    /// </summary>
    public uint Serial => _serial;

    /// <summary>
    /// Stores an integer setting. The property is written on the next idle, and only when the value changes.
    /// </summary>
    /// <param name="name">The setting name, for example <c>Xft/DPI</c>.</param>
    /// <param name="value">The integer value.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public void Set(string name, int value) => Store(name, new Setting(value, null));

    /// <summary>
    /// Stores a string setting. The property is written on the next idle, and only when the value changes.
    /// </summary>
    /// <param name="name">The setting name, for example <c>Gtk/CursorThemeName</c>.</param>
    /// <param name="value">The string value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public void Set(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Store(name, new Setting(0, value));
    }

    /// <summary>
    /// Removes a setting. The property is written on the next idle.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <returns><see langword="false"/> when no such setting is stored.</returns>
    public bool Remove(string name)
    {
        if (!_settings.Remove(name))
        {
            return false;
        }

        QueueFlush();
        return true;
    }

    /// <summary>
    /// Reads a stored integer setting.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <returns>The value, or <see langword="null"/> when the setting is missing or holds a string.</returns>
    public int? IntegerOf(string name) =>
        _settings.TryGetValue(name, out var setting) && setting.Text is null ? setting.Integer : null;

    /// <summary>
    /// Reads a stored string setting.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <returns>The value, or <see langword="null"/> when the setting is missing or holds an integer.</returns>
    public string? TextOf(string name) =>
        _settings.TryGetValue(name, out var setting) ? setting.Text : null;

    internal void WriteNow()
    {
        _flushQueued = false;
        if (!IsOwner)
        {
            return;
        }

        var bytes = Encode(_settings, _serial);
        fixed (byte* ptr = bytes)
        {
            _ = Libxcb.xcb_change_property(_conn, 0, _window, _settingsAtom, _settingsAtom, 8, (uint)bytes.Length, ptr);
        }

        _ = Libxcb.xcb_flush(_conn);
        _flushed?.Invoke();
    }

    /// <summary>
    /// Encodes settings in the little-endian XSETTINGS wire format, in the enumeration order of <paramref name="settings"/>.
    /// </summary>
    /// <param name="settings">The settings by name. Names must be ASCII.</param>
    /// <param name="serial">The serial to write in the header.</param>
    /// <returns>A new array that holds the property value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is <see langword="null"/>.</exception>
    public static byte[] Encode(IReadOnlyDictionary<string, Setting> settings, uint serial)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var buffer = new List<byte>(256) { 0, 0, 0, 0 };
        Append32(buffer, serial);
        Append32(buffer, (uint)settings.Count);
        foreach (var (name, setting) in settings)
        {
            var nameBytes = Encoding.ASCII.GetBytes(name);
            buffer.Add(setting.Text is null ? (byte)0 : (byte)1);
            buffer.Add(0);
            buffer.Add((byte)(nameBytes.Length & 0xff));
            buffer.Add((byte)(nameBytes.Length >> 8));
            buffer.AddRange(nameBytes);
            Pad(buffer);
            Append32(buffer, setting.LastChange);
            if (setting.Text is null)
            {
                Append32(buffer, unchecked((uint)setting.Integer));
            }
            else
            {
                var text = Encoding.UTF8.GetBytes(setting.Text);
                Append32(buffer, (uint)text.Length);
                buffer.AddRange(text);
                Pad(buffer);
            }
        }

        return [.. buffer];

        static void Append32(List<byte> buffer, uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            buffer.AddRange(bytes);
        }

        static void Pad(List<byte> buffer)
        {
            while ((buffer.Count & 3) != 0)
            {
                buffer.Add(0);
            }
        }
    }

    private void Store(string name, Setting setting)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_settings.TryGetValue(name, out var existing)
            && existing.Integer == setting.Integer && existing.Text == setting.Text)
        {
            return;
        }

        _serial++;
        _settings[name] = setting with { LastChange = _serial };
        QueueFlush();
    }

    private void QueueFlush()
    {
        if (_flushQueued || !IsOwner)
        {
            return;
        }

        _flushQueued = true;
        _loop.AddIdle(WriteNow);
    }

    /// <summary>
    /// One XSETTINGS value. A setting with a <see langword="null"/> text is an integer setting.
    /// </summary>
    /// <param name="Integer">The integer value. It is ignored when <paramref name="Text"/> is not <see langword="null"/>.</param>
    /// <param name="Text">The string value, or <see langword="null"/> for an integer setting.</param>
    public readonly record struct Setting(int Integer, string? Text)
    {
        /// <summary>
        /// Gets the serial at which the setting last changed.
        /// </summary>
        public uint LastChange { get; init; }
    }
}
