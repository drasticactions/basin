using Tmds.DBus.Protocol;
using static Basin.AppMenu.AppMenuLog;

namespace Basin.AppMenu;

/// <summary>
/// Reads a com.canonical.dbusmenu menu into an <see cref="AppMenu"/> and sends activations back. Layout and property signals cause a whole re-read.
/// </summary>
public sealed class DbusMenuReader : IAppMenuSource
{
    private const string Interface = "com.canonical.dbusmenu";

    private readonly AppMenuBus _bus;
    private readonly string _busName;
    private readonly string _path;
    private readonly List<IDisposable> _watches = [];
    private DBusConnection? _connection;
    private bool _fetching;
    private bool _dirty;
    private bool _failed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbusMenuReader"/> class and starts the first read.
    /// </summary>
    /// <param name="bus">The shared bus connection.</param>
    /// <param name="busName">The bus name of the client.</param>
    /// <param name="objectPath">The com.canonical.dbusmenu object path.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DbusMenuReader(AppMenuBus bus, string busName, string objectPath)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _busName = busName ?? throw new ArgumentNullException(nameof(busName));
        _path = objectPath ?? throw new ArgumentNullException(nameof(objectPath));
        _ = StartAsync();
    }

    /// <inheritdoc/>
    public AppMenuAddressKind Kind => AppMenuAddressKind.DbusMenu;

    /// <inheritdoc/>
    public AppMenu Menu { get; } = new();

    /// <inheritdoc/>
    public bool IsReady { get; private set; }

    /// <inheritdoc/>
    public event Action? Failed;

    /// <summary>
    /// Sends <c>AboutToShow</c> for the item. When the client answers that the menu needs an update, the reader reads the menu again.
    /// </summary>
    /// <param name="item">The submenu item that opens.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public void AboutToShow(AppMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_connection is not { } connection || _disposed)
        {
            return;
        }

        _ = AboutToShowAsync(connection, item.Id);
    }

    /// <summary>
    /// Sends a <c>clicked</c> event for the item and does not wait for a reply. It does nothing before the connection exists or after dispose.
    /// </summary>
    /// <param name="item">The item that the user chose.</param>
    /// <param name="timestamp">The input event time in milliseconds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public void Activate(AppMenuItem item, uint timestamp)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_connection is not { } connection || _disposed)
        {
            return;
        }

        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(_busName, _path, Interface, "Event", "isvu", MessageFlags.NoReplyExpected);
        writer.WriteInt32(item.Id);
        writer.WriteString("clicked");
        writer.WriteVariantInt32(0);
        writer.WriteUInt32(timestamp);
        _ = connection.TrySendMessage(writer.CreateMessage());
    }

    /// <summary>
    /// Stops watching the client's signals. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var watch in _watches)
        {
            watch.Dispose();
        }

        _watches.Clear();
    }

    private async Task StartAsync()
    {
        var connection = await _bus.ConnectionAsync().ConfigureAwait(false);
        if (connection is null)
        {
            _bus.Post(Fail);
            return;
        }

        try
        {
            foreach (var signal in (string[])["LayoutUpdated", "ItemsPropertiesUpdated"])
            {
                var watch = await connection.WatchSignalAsync(
                    _busName, _path, Interface, signal,
                    (Action<Notification>)(notification =>
                    {
                        if (!notification.IsCompletion)
                        {
                            _bus.Post(Refetch);
                        }
                    }),
                    ObserverFlags.None, false, null).ConfigureAwait(false);
                _bus.Post(() => _watches.Add(watch));
            }

            _bus.Post(() =>
            {
                _connection = connection;
                Refetch();
            });
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            Log.Debug($"{_busName}{_path}: {error.Message}");
            _bus.Post(Fail);
        }
    }

    private void Refetch()
    {
        if (_disposed || _connection is not { } connection)
        {
            return;
        }

        if (_fetching)
        {
            _dirty = true;
            return;
        }

        _fetching = true;
        _ = FetchAsync(connection);
    }

    private async Task FetchAsync(DBusConnection connection)
    {
        try
        {
            var items = await connection.CallMethodAsync(GetLayoutMessage(connection), static (message, _) => ReadLayout(message), null)
                .ConfigureAwait(false);

            _bus.Post(() =>
            {
                _fetching = false;
                if (_disposed)
                {
                    return;
                }

                IsReady = true;
                Menu.Replace(items);
                if (_dirty)
                {
                    _dirty = false;
                    Refetch();
                }
            });
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException or IndexOutOfRangeException)
        {
            Log.Debug($"{_busName}{_path} GetLayout: {error.Message}");
            _bus.Post(() =>
            {
                _fetching = false;
                if (!IsReady)
                {
                    Fail();
                }
            });
        }
    }

    private async Task AboutToShowAsync(DBusConnection connection, int id)
    {
        try
        {
            var update = await connection.CallMethodAsync(AboutToShowMessage(connection, id), static (message, _) => message.GetBodyReader().ReadBool(), null)
                .ConfigureAwait(false);

            if (update)
            {
                _bus.Post(Refetch);
            }
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            Log.Debug($"{_busName}{_path} AboutToShow {id}: {error.Message}");
        }
    }

    private MessageBuffer GetLayoutMessage(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(_busName, _path, Interface, "GetLayout", "iias");
        writer.WriteInt32(0);
        writer.WriteInt32(-1);
        writer.WriteArray(Array.Empty<string>());
        return writer.CreateMessage();
    }

    private MessageBuffer AboutToShowMessage(DBusConnection connection, int id)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(_busName, _path, Interface, "AboutToShow", "i");
        writer.WriteInt32(id);
        return writer.CreateMessage();
    }

    private void Fail()
    {
        if (_failed || _disposed)
        {
            return;
        }

        _failed = true;
        Failed?.Invoke();
    }

    private static List<AppMenuItem> ReadLayout(Message message)
    {
        var reader = message.GetBodyReader();
        _ = reader.ReadUInt32();
        reader.AlignStruct();
        _ = reader.ReadInt32();
        _ = reader.ReadDictionaryOfStringToVariantValue();
        var children = reader.ReadArrayOfVariantValue();
        var items = new List<AppMenuItem>(children.Length);
        foreach (var child in children)
        {
            items.Add(ReadItem(Unwrap(child)));
        }

        return items;
    }

    private static AppMenuItem ReadItem(VariantValue value)
    {
        var properties = new Dictionary<string, VariantValue>(StringComparer.Ordinal);
        var map = Unwrap(value.GetItem(1));
        for (var i = 0; i < map.Count; i++)
        {
            var entry = map.GetDictionaryEntry(i);
            properties[entry.Key.GetString()] = Unwrap(entry.Value);
        }

        var item = new AppMenuItem { Id = Unwrap(value.GetItem(0)).GetInt32() };
        Apply(item, properties);
        var children = Unwrap(value.GetItem(2));
        for (var i = 0; i < children.Count; i++)
        {
            item.Children.Add(ReadItem(Unwrap(children.GetItem(i))));
        }

        item.IsSubmenu |= item.Children.Count > 0;
        return item;
    }

    internal static void Apply(AppMenuItem item, IReadOnlyDictionary<string, VariantValue> properties)
    {
        string Text(string key, string fallback) =>
            properties.TryGetValue(key, out var v) && v.Type == VariantValueType.String ? v.GetString() : fallback;
        bool Flag(string key, bool fallback) =>
            properties.TryGetValue(key, out var v) && v.Type == VariantValueType.Bool ? v.GetBool() : fallback;

        item.Label = AppMenuLabels.StripMnemonic(Text("label", ""));
        item.Enabled = Flag("enabled", true);
        item.Visible = Flag("visible", true);
        item.IconName = Text("icon-name", "") is { Length: > 0 } icon ? icon : null;
        item.IconData = properties.TryGetValue("icon-data", out var data) && data.Type == VariantValueType.Array
            ? data.GetArray<byte>()
            : null;
        item.IsSubmenu = Text("children-display", "") == "submenu";
        item.Kind = Text("type", "standard") == "separator"
            ? AppMenuItemKind.Separator
            : Text("toggle-type", "") switch
            {
                "checkmark" => AppMenuItemKind.Check,
                "radio" => AppMenuItemKind.Radio,
                _ => AppMenuItemKind.Normal,
            };
        item.ToggleState = properties.TryGetValue("toggle-state", out var state) && state.Type == VariantValueType.Int32 && state.GetInt32() == 1;
        item.Shortcut = null;
        if (properties.TryGetValue("shortcut", out var shortcut) && shortcut.Type == VariantValueType.Array && shortcut.Count > 0)
        {
            item.Shortcut = AppMenuShortcut.FromDbusMenu(Unwrap(shortcut.GetItem(0)).GetArray<string>());
        }
    }

    private static VariantValue Unwrap(VariantValue value) => value.Type == VariantValueType.Variant ? value.GetVariantValue() : value;
}
