using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Testing;

/// <summary>
/// A small AT-SPI client that reads and drives an accessibility tree as a screen reader does.
/// </summary>
public sealed class AtSpiClient : IDisposable
{
    private const string Accessible = "org.a11y.atspi.Accessible";
    private const string RegistryRoot = "/org/a11y/atspi/accessible/root";
    private const int MaxDepth = 40;
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(10);
    private readonly DBusConnection _connection;
    private readonly List<AtSpiEvent> _events = [];
    private bool _listening;

    private AtSpiClient(DBusConnection connection) => _connection = connection;

    /// <summary>
    /// Gets the connection to the AT-SPI bus.
    /// </summary>
    public DBusConnection Connection => _connection;

    /// <summary>
    /// Connects to an AT-SPI bus.
    /// </summary>
    /// <param name="address">The address of the AT-SPI bus.</param>
    /// <returns>The connected client.</returns>
    public static async Task<AtSpiClient> ConnectAsync(string address)
    {
        var connection = new DBusConnection(address);
        await connection.ConnectAsync().ConfigureAwait(false);
        return new AtSpiClient(connection);
    }

    /// <summary>
    /// Reads every object of every application on the bus, depth first.
    /// </summary>
    /// <returns>The objects in tree order.</returns>
    public async Task<List<AtSpiNode>> TreeAsync()
    {
        var found = new List<AtSpiNode>();
        foreach (var (bus, path) in await ChildRefsAsync("org.a11y.atspi.Registry", RegistryRoot).ConfigureAwait(false))
        {
            await WalkAsync(bus, path, 0, found).ConfigureAwait(false);
        }

        return found;
    }

    /// <summary>
    /// Reads the tree until a condition holds, and fails the test with the last tree when it never does.
    /// </summary>
    /// <param name="done">The condition over the whole tree.</param>
    /// <param name="timeout">The longest wait. The default is ten seconds.</param>
    /// <returns>The tree that met the condition.</returns>
    public async Task<List<AtSpiNode>> WhereAsync(Func<List<AtSpiNode>, bool> done, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultWait);
        var nodes = new List<AtSpiNode>();
        while (true)
        {
            nodes = await TreeAsync().ConfigureAwait(false);
            if (done(nodes) || DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        Assert.True(done(nodes), "tree: " + Describe(nodes));
        return nodes;
    }

    /// <summary>
    /// Waits for the first object with a name, and optionally a role.
    /// </summary>
    /// <param name="name">The accessible name.</param>
    /// <param name="role">The AT-SPI role name, or <see langword="null"/> for any role.</param>
    /// <param name="timeout">The longest wait. The default is ten seconds.</param>
    /// <returns>The object.</returns>
    public async Task<AtSpiNode> FindAsync(string name, string? role = null, TimeSpan? timeout = null)
    {
        bool Match(AtSpiNode node) => node.Name == name && (role is null || node.Role == role);
        var nodes = await WhereAsync(nodes => nodes.Any(Match), timeout).ConfigureAwait(false);
        return nodes.First(Match);
    }

    /// <summary>
    /// Reads the text of every object that has the Text interface.
    /// </summary>
    /// <returns>The texts in tree order.</returns>
    public async Task<List<string>> TextsAsync()
    {
        var texts = new List<string>();
        foreach (var node in await TreeAsync().ConfigureAwait(false))
        {
            if (await TextAsync(node).ConfigureAwait(false) is { } text)
            {
                texts.Add(text);
            }
        }

        return texts;
    }

    /// <summary>
    /// Reads the whole text of an object.
    /// </summary>
    /// <param name="node">The object.</param>
    /// <returns>The text, or <see langword="null"/> when the object has no Text interface.</returns>
    public async Task<string?> TextAsync(AtSpiNode node)
    {
        try
        {
            return await _connection.CallMethodAsync(GetText(node), static (m, _) => m.GetBodyReader().ReadString(), null).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the caret offset of an object through the Text interface.
    /// </summary>
    /// <param name="node">The object.</param>
    /// <returns>The caret offset in characters.</returns>
    public Task<int> CaretOffsetAsync(AtSpiNode node) =>
        _connection.CallMethodAsync(GetProperty(node, "org.a11y.atspi.Text", "CaretOffset"), static (m, _) => m.GetBodyReader().ReadVariantValue().GetInt32(), null);

    /// <summary>
    /// Reads the direct children of an object.
    /// </summary>
    /// <param name="node">The parent object.</param>
    /// <returns>The children.</returns>
    public async Task<List<AtSpiNode>> ChildrenAsync(AtSpiNode node)
    {
        var children = new List<AtSpiNode>();
        foreach (var (bus, path) in await ChildRefsAsync(node.Bus, node.Path).ConfigureAwait(false))
        {
            if (await DescribeAsync(bus, path).ConfigureAwait(false) is { } child)
            {
                children.Add(child);
            }
        }

        return children;
    }

    /// <summary>
    /// Reads the names of the actions that an object offers.
    /// </summary>
    /// <param name="node">The object.</param>
    /// <returns>The action names in index order.</returns>
    public Task<List<string>> ActionsAsync(AtSpiNode node) =>
        _connection.CallMethodAsync(Call(node, "org.a11y.atspi.Action", "GetActions"), static (m, _) =>
        {
            var reader = m.GetBodyReader();
            var names = new List<string>();
            var array = reader.ReadArrayStart(DBusType.Struct);
            while (reader.HasNext(array))
            {
                reader.AlignStruct();
                names.Add(reader.ReadString());
                reader.ReadString();
                reader.ReadString();
            }

            return names;
        }, null);

    /// <summary>
    /// Runs an action by name, as a screen reader's activate command does.
    /// </summary>
    /// <param name="node">The object.</param>
    /// <param name="action">The action name, for example "click".</param>
    /// <returns><see langword="true"/> when the application accepted the action.</returns>
    public async Task<bool> DoActionAsync(AtSpiNode node, string action)
    {
        var names = await ActionsAsync(node).ConfigureAwait(false);
        var index = names.IndexOf(action);
        Assert.True(index >= 0, $"{node.Role} '{node.Name}' has no action '{action}': {string.Join(", ", names)}");
        return await _connection.CallMethodAsync(DoAction(node, index), static (m, _) => m.GetBodyReader().ReadBool(), null).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the extents of an object through the Component interface.
    /// </summary>
    /// <param name="node">The object.</param>
    /// <param name="coordinates">The coordinate space.</param>
    /// <returns>The extents.</returns>
    public Task<AtSpiRect> ExtentsAsync(AtSpiNode node, AtSpiCoordinates coordinates = AtSpiCoordinates.Screen) =>
        _connection.CallMethodAsync(GetExtents(node, coordinates), static (m, _) =>
        {
            var reader = m.GetBodyReader();
            reader.AlignStruct();
            return new AtSpiRect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }, null);

    /// <summary>
    /// Tells if an object has a state.
    /// </summary>
    /// <param name="node">The object.</param>
    /// <param name="state">The state.</param>
    /// <returns><see langword="true"/> when the state is set.</returns>
    public async Task<bool> HasStateAsync(AtSpiNode node, AtSpiState state)
    {
        var words = await _connection.CallMethodAsync(Call(node, Accessible, "GetState"), static (m, _) => m.GetBodyReader().ReadArrayOfUInt32(), null).ConfigureAwait(false);
        var bit = (int)state;
        return bit / 32 < words.Length && (words[bit / 32] & (1u << (bit % 32))) != 0;
    }

    /// <summary>
    /// Waits until an object has or loses a state.
    /// </summary>
    /// <param name="node">The object.</param>
    /// <param name="state">The state.</param>
    /// <param name="value">The value to wait for.</param>
    /// <param name="timeout">The longest wait. The default is ten seconds.</param>
    /// <returns>A task that completes when the state has the value.</returns>
    public async Task WaitForStateAsync(AtSpiNode node, AtSpiState state, bool value = true, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultWait);
        while (await HasStateAsync(node, state).ConfigureAwait(false) != value)
        {
            Assert.True(DateTime.UtcNow < deadline, $"{node.Role} '{node.Name}' never had {state}={value}");
            await Task.Delay(50, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Starts to record object events: state changes, text changes and announcements.
    /// </summary>
    /// <returns>A task that completes when the match rule is on the bus.</returns>
    public async Task ListenAsync()
    {
        if (_listening)
        {
            return;
        }

        _listening = true;
        await _connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Interface = "org.a11y.atspi.Event.Object" },
            static (m, _) =>
            {
                var reader = m.GetBodyReader();
                var kind = reader.ReadString();
                var detail1 = reader.ReadInt32();
                var detail2 = reader.ReadInt32();
                var data = reader.ReadVariantValue();
                var text = data.Type == VariantValueType.String ? data.GetString() : null;
                return new AtSpiEvent(m.SenderAsString ?? "", m.PathAsString ?? "", m.MemberAsString ?? "", kind, detail1, detail2, text);
            },
            (Notification<AtSpiEvent> notification) =>
            {
                if (notification.HasValue)
                {
                    lock (_events)
                    {
                        _events.Add(notification.Value);
                    }
                }
            },
            false,
            ObserverFlags.None,
            null).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a copy of the events recorded since <see cref="ListenAsync"/>.
    /// </summary>
    /// <returns>The events in arrival order.</returns>
    public List<AtSpiEvent> Events()
    {
        lock (_events)
        {
            return [.. _events];
        }
    }

    /// <summary>
    /// Waits for a recorded event that matches a condition, and fails the test with the events seen when none does.
    /// </summary>
    /// <param name="match">The condition.</param>
    /// <param name="timeout">The longest wait. The default is ten seconds.</param>
    /// <returns>The first matching event.</returns>
    public async Task<AtSpiEvent> WaitForEventAsync(Func<AtSpiEvent, bool> match, TimeSpan? timeout = null)
    {
        Assert.True(_listening, "call ListenAsync before waiting for events");
        var deadline = DateTime.UtcNow + (timeout ?? DefaultWait);
        while (true)
        {
            var events = Events();
            if (events.FirstOrDefault(match) is { } found)
            {
                return found;
            }

            Assert.True(DateTime.UtcNow < deadline, "events: " + string.Join(", ", events.Select(e => $"{e.Member}:{e.Kind}:{e.Text}")));
            await Task.Delay(50, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits for an announcement whose text contains a value.
    /// </summary>
    /// <param name="text">The text to find.</param>
    /// <param name="timeout">The longest wait. The default is ten seconds.</param>
    /// <returns>The announcement event.</returns>
    public Task<AtSpiEvent> WaitForAnnouncementAsync(string text, TimeSpan? timeout = null) =>
        WaitForEventAsync(e => e.Member == "Announcement" && e.Text is { } said && said.Contains(text, StringComparison.Ordinal), timeout);

    /// <summary>
    /// Formats a tree as role:name pairs for a failure message.
    /// </summary>
    /// <param name="nodes">The objects.</param>
    /// <returns>The description.</returns>
    public static string Describe(IEnumerable<AtSpiNode> nodes) => string.Join(", ", nodes.Select(node => $"{node.Role}:{node.Name}"));

    /// <summary>
    /// Closes the connection.
    /// </summary>
    public void Dispose() => _connection.Dispose();

    private async Task WalkAsync(string bus, string path, int depth, List<AtSpiNode> found)
    {
        if (depth > MaxDepth || await DescribeAsync(bus, path).ConfigureAwait(false) is not { } node)
        {
            return;
        }

        found.Add(node);
        foreach (var (childBus, childPath) in await ChildRefsAsync(bus, path).ConfigureAwait(false))
        {
            await WalkAsync(childBus, childPath, depth + 1, found).ConfigureAwait(false);
        }
    }

    private async Task<AtSpiNode?> DescribeAsync(string bus, string path)
    {
        try
        {
            var role = await _connection.CallMethodAsync(Call(bus, path, Accessible, "GetRole"), static (m, _) => AtSpiRoles.NameOf(m.GetBodyReader().ReadUInt32()), null).ConfigureAwait(false);
            var name = await _connection.CallMethodAsync(GetName(bus, path), static (m, _) => m.GetBodyReader().ReadVariantValue().GetString(), null).ConfigureAwait(false);
            return new AtSpiNode(bus, path, role, name);
        }
        catch (DBusErrorReplyException)
        {
            return null;
        }
    }

    private async Task<List<(string Bus, string Path)>> ChildRefsAsync(string bus, string path)
    {
        try
        {
            return await _connection.CallMethodAsync(Call(bus, path, Accessible, "GetChildren"), static (m, _) =>
            {
                var reader = m.GetBodyReader();
                var children = new List<(string, string)>();
                var array = reader.ReadArrayStart(DBusType.Struct);
                while (reader.HasNext(array))
                {
                    reader.AlignStruct();
                    children.Add((reader.ReadString(), reader.ReadObjectPathAsString()));
                }

                return children;
            }, null).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException)
        {
            return [];
        }
    }

    private MessageBuffer Call(AtSpiNode node, string @interface, string member) => Call(node.Bus, node.Path, @interface, member);

    private MessageBuffer Call(string bus, string path, string @interface, string member)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(bus, path, @interface, member);
        return writer.CreateMessage();
    }

    private MessageBuffer GetName(string bus, string path)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(bus, path, "org.freedesktop.DBus.Properties", "Get", "ss");
        writer.WriteString(Accessible);
        writer.WriteString("Name");
        return writer.CreateMessage();
    }

    private MessageBuffer GetProperty(AtSpiNode node, string @interface, string name)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(node.Bus, node.Path, "org.freedesktop.DBus.Properties", "Get", "ss");
        writer.WriteString(@interface);
        writer.WriteString(name);
        return writer.CreateMessage();
    }

    private MessageBuffer GetText(AtSpiNode node)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(node.Bus, node.Path, "org.a11y.atspi.Text", "GetText", "ii");
        writer.WriteInt32(0);
        writer.WriteInt32(-1);
        return writer.CreateMessage();
    }

    private MessageBuffer DoAction(AtSpiNode node, int index)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(node.Bus, node.Path, "org.a11y.atspi.Action", "DoAction", "i");
        writer.WriteInt32(index);
        return writer.CreateMessage();
    }

    private MessageBuffer GetExtents(AtSpiNode node, AtSpiCoordinates coordinates)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(node.Bus, node.Path, "org.a11y.atspi.Component", "GetExtents", "u");
        writer.WriteUInt32((uint)coordinates);
        return writer.CreateMessage();
    }
}
