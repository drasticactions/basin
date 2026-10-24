using Tmds.DBus.Protocol;
using static Basin.AppMenu.AppMenuLog;

namespace Basin.AppMenu;

/// <summary>
/// Reads a GTK org.gtk.Menus menu bar and its org.gtk.Actions state into an <see cref="AppMenu"/>, and activates actions.
/// Sections flatten into their parent with separators between them.
/// </summary>
public sealed class GMenuReader : IAppMenuSource
{
    private const string MenusInterface = "org.gtk.Menus";
    private const string ActionsInterface = "org.gtk.Actions";

    private readonly AppMenuBus _bus;
    private readonly AppMenuAddress _address;
    private readonly Dictionary<GMenuLink, List<Dictionary<string, VariantValue>>> _menus = [];
    private readonly HashSet<uint> _subscribed = [];
    private readonly Dictionary<string, GMenuAction> _app = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GMenuAction> _window = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _watches = [];
    private DBusConnection? _connection;
    private bool _failed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GMenuReader"/> class and starts the first read.
    /// The menu bar path is used, or the app menu path when the menu bar path is empty.
    /// </summary>
    /// <param name="bus">The shared bus connection.</param>
    /// <param name="address">A GMenu address.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bus"/> or <paramref name="address"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="address"/> is not of kind <see cref="AppMenuAddressKind.GMenu"/>.</exception>
    public GMenuReader(AppMenuBus bus, AppMenuAddress address)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _address = address ?? throw new ArgumentNullException(nameof(address));
        if (address.Kind != AppMenuAddressKind.GMenu)
        {
            throw new ArgumentException("a GMenu reader needs a GMenu address", nameof(address));
        }

        _ = StartAsync();
    }

    /// <inheritdoc/>
    public AppMenuAddressKind Kind => AppMenuAddressKind.GMenu;

    /// <inheritdoc/>
    public AppMenu Menu { get; } = new();

    /// <inheritdoc/>
    public bool IsReady { get; private set; }

    /// <inheritdoc/>
    public event Action? Failed;

    private string MenuPath => _address.MenubarPath.Length > 0 ? _address.MenubarPath : _address.AppMenuPath;

    /// <summary>
    /// Does nothing. GMenu sends submenu content before it opens.
    /// </summary>
    /// <param name="item">The submenu item that opens.</param>
    public void AboutToShow(AppMenuItem item)
    {
    }

    /// <summary>
    /// Sends <c>Activate</c> for the item's action, with its target, to the path that the action prefix names. It does not wait for a reply.
    /// An item with no action, an unknown prefix, or a call before the connection exists does nothing. The timestamp is not used.
    /// </summary>
    /// <param name="item">The item that the user chose.</param>
    /// <param name="timestamp">The input event time in milliseconds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public void Activate(AppMenuItem item, uint timestamp)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_connection is not { } connection || _disposed || item.Tag is not GMenuTarget target)
        {
            return;
        }

        var (path, name) = Resolve(target.Action);
        if (path is null)
        {
            return;
        }

        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(_address.BusName, path, ActionsInterface, "Activate", "sava{sv}", MessageFlags.NoReplyExpected);
        writer.WriteString(name);
        var parameters = writer.WriteArrayStart(DBusType.Variant);
        if (target.Target is { } value)
        {
            writer.WriteVariant(value);
        }

        writer.WriteArrayEnd(parameters);
        var platform = writer.WriteDictionaryStart();
        writer.WriteDictionaryEnd(platform);
        _ = connection.TrySendMessage(writer.CreateMessage());
    }

    /// <summary>
    /// Stops watching the client's signals and sends <c>End</c> for the groups that the reader started. A second call does nothing.
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
        if (_connection is { } connection && _subscribed.Count > 0)
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(_address.BusName, MenuPath, MenusInterface, "End", "au", MessageFlags.NoReplyExpected);
            writer.WriteArray(_subscribed.ToArray());
            _ = connection.TrySendMessage(writer.CreateMessage());
        }
    }

    private (string? Path, string Name) Resolve(string action)
    {
        if (action.StartsWith("app.", StringComparison.Ordinal) && _address.ApplicationPath.Length > 0)
        {
            return (_address.ApplicationPath, action[4..]);
        }

        if (action.StartsWith("win.", StringComparison.Ordinal) && _address.WindowPath.Length > 0)
        {
            return (_address.WindowPath, action[4..]);
        }

        return (null, action);
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
            var menuWatch = await connection.WatchSignalAsync(
                _address.BusName, MenuPath, MenusInterface, "Changed",
                static (message, _) => ReadMenuChanges(message),
                (Action<Notification<List<(uint Group, uint Menu, uint Position, uint Removed, List<Dictionary<string, VariantValue>> Added)>>>)(notification =>
                {
                    if (!notification.IsCompletion)
                    {
                        var changes = notification.Value;
                        _bus.Post(() => ApplyMenuChanges(changes));
                    }
                }),
                ObserverFlags.None, false, null).ConfigureAwait(false);
            _bus.Post(() => _watches.Add(menuWatch));

            foreach (var (path, table) in ActionPaths())
            {
                var actionWatch = await connection.WatchSignalAsync(
                    _address.BusName, path, ActionsInterface, "Changed",
                    (Action<Notification>)(notification =>
                    {
                        if (!notification.IsCompletion)
                        {
                            _bus.Post(() => _ = DescribeAsync(connection, path, table));
                        }
                    }),
                    ObserverFlags.None, false, null).ConfigureAwait(false);
                _bus.Post(() => _watches.Add(actionWatch));
                var described = await connection.CallMethodAsync(DescribeMessage(connection, path), static (message, _) => ReadActions(message), null)
                    .ConfigureAwait(false);
                _bus.Post(() => Fill(table, described));
            }

            var groups = await connection.CallMethodAsync(StartMessage(connection, [0]), static (message, _) => ReadGroups(message), null)
                .ConfigureAwait(false);
            _bus.Post(() =>
            {
                _connection = connection;
                _subscribed.Add(0);
                Store(groups);
                SubscribeLinked();
            });
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException or IndexOutOfRangeException)
        {
            Log.Debug($"{_address.BusName}{MenuPath}: {error.Message}");
            _bus.Post(Fail);
        }
    }

    private IEnumerable<(string Path, Dictionary<string, GMenuAction> Table)> ActionPaths()
    {
        if (_address.ApplicationPath.Length > 0)
        {
            yield return (_address.ApplicationPath, _app);
        }

        if (_address.WindowPath.Length > 0)
        {
            yield return (_address.WindowPath, _window);
        }
    }

    private async Task DescribeAsync(DBusConnection connection, string path, Dictionary<string, GMenuAction> table)
    {
        try
        {
            var described = await connection.CallMethodAsync(DescribeMessage(connection, path), static (message, _) => ReadActions(message), null)
                .ConfigureAwait(false);
            _bus.Post(() =>
            {
                Fill(table, described);
                Rebuild();
            });
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            Log.Debug($"{_address.BusName}{path} DescribeAll: {error.Message}");
        }
    }

    private static void Fill(Dictionary<string, GMenuAction> table, Dictionary<string, GMenuAction> described)
    {
        table.Clear();
        foreach (var (name, action) in described)
        {
            table[name] = action;
        }
    }

    private void SubscribeLinked()
    {
        if (_disposed || _connection is not { } connection)
        {
            return;
        }

        var wanted = new List<uint>();
        foreach (var items in _menus.Values)
        {
            foreach (var item in items)
            {
                foreach (var key in (string[])[":section", ":submenu"])
                {
                    if (item.TryGetValue(key, out var link) && Link(link) is { } target && _subscribed.Add(target.Group))
                    {
                        wanted.Add(target.Group);
                    }
                }
            }
        }

        if (wanted.Count == 0)
        {
            IsReady = true;
            Rebuild();
            return;
        }

        _ = StartGroupsAsync(connection, wanted);
    }

    private async Task StartGroupsAsync(DBusConnection connection, List<uint> groups)
    {
        try
        {
            var result = await connection.CallMethodAsync(StartMessage(connection, groups.ToArray()), static (message, _) => ReadGroups(message), null)
                .ConfigureAwait(false);
            _bus.Post(() =>
            {
                Store(result);
                SubscribeLinked();
            });
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            Log.Debug($"{_address.BusName}{MenuPath} Start: {error.Message}");
            _bus.Post(() =>
            {
                if (!IsReady)
                {
                    Fail();
                }
            });
        }
    }

    private void Store(List<(uint Group, uint Menu, List<Dictionary<string, VariantValue>> Items)> groups)
    {
        foreach (var (group, menu, items) in groups)
        {
            _menus[new GMenuLink(group, menu)] = items;
        }
    }

    private void ApplyMenuChanges(List<(uint Group, uint Menu, uint Position, uint Removed, List<Dictionary<string, VariantValue>> Added)> changes)
    {
        foreach (var (group, menu, position, removed, added) in changes)
        {
            if (!_menus.TryGetValue(new GMenuLink(group, menu), out var items))
            {
                items = [];
                _menus[new GMenuLink(group, menu)] = items;
            }

            var start = (int)Math.Min(position, (uint)items.Count);
            items.RemoveRange(start, (int)Math.Min(removed, (uint)(items.Count - start)));
            items.InsertRange(start, added);
        }

        SubscribeLinked();
        Rebuild();
    }

    private void Rebuild()
    {
        if (!IsReady || _disposed)
        {
            return;
        }

        var next = 1;
        var titles = new List<AppMenuItem>();
        if (_menus.TryGetValue(new GMenuLink(0, 0), out var root))
        {
            foreach (var entry in Flatten(root, 0))
            {
                var title = Build(entry, ref next, 0);
                if (title is not null)
                {
                    titles.Add(title);
                }
            }
        }

        Menu.Replace(titles);
    }

    private IEnumerable<Dictionary<string, VariantValue>?> Flatten(List<Dictionary<string, VariantValue>> items, int depth)
    {
        var first = true;
        foreach (var item in items)
        {
            if (item.TryGetValue(":section", out var section) && Link(section) is { } link)
            {
                if (depth > 8 || !_menus.TryGetValue(link, out var children))
                {
                    continue;
                }

                var entries = Flatten(children, depth + 1).ToList();
                if (entries.Count == 0)
                {
                    continue;
                }

                if (!first)
                {
                    yield return null;
                }

                foreach (var entry in entries)
                {
                    yield return entry;
                }

                first = false;
                continue;
            }

            yield return item;
            first = false;
        }
    }

    private AppMenuItem? Build(Dictionary<string, VariantValue>? entry, ref int next, int depth)
    {
        if (entry is null)
        {
            return new AppMenuItem { Id = next++, Kind = AppMenuItemKind.Separator };
        }

        var item = new AppMenuItem
        {
            Id = next++,
            Label = AppMenuLabels.StripMnemonic(Text(entry, "label")),
        };
        if (Text(entry, "accel") is { Length: > 0 } accel)
        {
            item.Shortcut = AppMenuShortcut.FromGtkAccelerator(accel);
        }

        if (entry.TryGetValue("icon", out var icon) && icon.Type == VariantValueType.String)
        {
            item.IconName = icon.GetString();
        }

        if (entry.TryGetValue(":submenu", out var submenu) && Link(submenu) is { } link)
        {
            item.IsSubmenu = true;
            if (depth < 8 && _menus.TryGetValue(link, out var children))
            {
                foreach (var child in Flatten(children, 0))
                {
                    if (Build(child, ref next, depth + 1) is { } built)
                    {
                        item.Children.Add(built);
                    }
                }
            }

            return item;
        }

        var actionName = Text(entry, "action");
        entry.TryGetValue("target", out var targetValue);
        VariantValue? target = entry.ContainsKey("target") ? Unwrap(targetValue) : (VariantValue?)null;
        item.Tag = new GMenuTarget(actionName, target);
        var action = Lookup(actionName);
        item.Enabled = action is { Enabled: true };
        if (action?.State is { } state)
        {
            if (state.Type == VariantValueType.Bool && target is null)
            {
                item.Kind = AppMenuItemKind.Check;
                item.ToggleState = state.GetBool();
            }
            else if (target is { } value)
            {
                item.Kind = AppMenuItemKind.Radio;
                item.ToggleState = state.Type == value.Type && state.Type == VariantValueType.String && state.GetString() == value.GetString();
            }
        }

        return item;
    }

    private GMenuAction? Lookup(string action)
    {
        if (action.StartsWith("app.", StringComparison.Ordinal))
        {
            return _app.GetValueOrDefault(action[4..]);
        }

        return action.StartsWith("win.", StringComparison.Ordinal) ? _window.GetValueOrDefault(action[4..]) : null;
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

    private MessageBuffer StartMessage(DBusConnection connection, uint[] groups)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(_address.BusName, MenuPath, MenusInterface, "Start", "au");
        writer.WriteArray(groups);
        return writer.CreateMessage();
    }

    private MessageBuffer DescribeMessage(DBusConnection connection, string path)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(_address.BusName, path, ActionsInterface, "DescribeAll");
        return writer.CreateMessage();
    }

    private static List<(uint Group, uint Menu, List<Dictionary<string, VariantValue>> Items)> ReadGroups(Message message)
    {
        var reader = message.GetBodyReader();
        var result = new List<(uint, uint, List<Dictionary<string, VariantValue>>)>();
        var end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            var group = reader.ReadUInt32();
            var menu = reader.ReadUInt32();
            result.Add((group, menu, ReadItems(ref reader)));
        }

        return result;
    }

    private static List<(uint, uint, uint, uint, List<Dictionary<string, VariantValue>>)> ReadMenuChanges(Message message)
    {
        var reader = message.GetBodyReader();
        var result = new List<(uint, uint, uint, uint, List<Dictionary<string, VariantValue>>)>();
        var end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            var group = reader.ReadUInt32();
            var menu = reader.ReadUInt32();
            var position = reader.ReadUInt32();
            var removed = reader.ReadUInt32();
            result.Add((group, menu, position, removed, ReadItems(ref reader)));
        }

        return result;
    }

    private static List<Dictionary<string, VariantValue>> ReadItems(ref Reader reader)
    {
        var items = new List<Dictionary<string, VariantValue>>();
        var end = reader.ReadArrayStart(DBusType.Array);
        while (reader.HasNext(end))
        {
            items.Add(reader.ReadDictionaryOfStringToVariantValue());
        }

        return items;
    }

    private static Dictionary<string, GMenuAction> ReadActions(Message message)
    {
        var reader = message.GetBodyReader();
        var result = new Dictionary<string, GMenuAction>(StringComparer.Ordinal);
        var end = reader.ReadDictionaryStart();
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            var name = reader.ReadString();
            reader.AlignStruct();
            var enabled = reader.ReadBool();
            _ = reader.ReadSignature();
            var state = reader.ReadArrayOfVariantValue();
            result[name] = new GMenuAction(enabled, state.Length > 0 ? Unwrap(state[0]) : (VariantValue?)null);
        }

        return result;
    }

    private static GMenuLink? Link(VariantValue value)
    {
        value = Unwrap(value);
        return value.Type == VariantValueType.Struct ? new GMenuLink(value.GetItem(0).GetUInt32(), value.GetItem(1).GetUInt32()) : null;
    }

    private static string Text(Dictionary<string, VariantValue> entry, string key) =>
        entry.TryGetValue(key, out var value) && Unwrap(value) is { Type: VariantValueType.String } text ? text.GetString() : "";

    private static VariantValue Unwrap(VariantValue value) => value.Type == VariantValueType.Variant ? value.GetVariantValue() : value;
}
