using Basin.Accessibility;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class A11yManagerBusTests
{
    private const uint KeyA = 30;
    private const uint KeyH = 35;
    private const uint KeyInsert = 110;
    private const uint SymInsert = 0xff63;
    private const uint SymH = 0x68;

    private sealed class Reader : IDisposable
    {
        private readonly List<string> _signals = [];

        private Reader(DBusConnection connection) => Connection = connection;

        public DBusConnection Connection { get; }

        public string? UniqueName => Connection.UniqueName;

        public List<string> Signals()
        {
            lock (_signals)
            {
                return [.. _signals];
            }
        }

        public static async Task<Reader> ConnectAsync(string address)
        {
            var connection = new DBusConnection(address);
            await connection.ConnectAsync();
            var reader = new Reader(connection);
            await connection.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Path = A11yManager.ObjectPath },
                static (Message m, object? _) =>
                {
                    if (m.MemberAsString != "KeyEvent")
                    {
                        return m.MemberAsString ?? "";
                    }

                    var body = m.GetBodyReader();
                    var released = body.ReadBool();
                    var state = body.ReadUInt32();
                    var keysym = body.ReadUInt32();
                    body.ReadUInt32();
                    var keycode = body.ReadUInt16();
                    return $"KeyEvent {(released ? "up" : "down")} {keysym:x} {keycode} {state}";
                },
                (Notification<string> n) =>
                {
                    if (n.HasValue)
                    {
                        lock (reader._signals)
                        {
                            reader._signals.Add(n.Value);
                        }
                    }
                },
                false,
                ObserverFlags.None,
                null);
            return reader;
        }

        public Task CallAsync(string @interface, string member) =>
            Connection.CallMethodAsync(Message(@interface, member));

        public Task SetKeyGrabsAsync(uint[] modifiers, (uint Keysym, uint Mask)[] keystrokes) =>
            Connection.CallMethodAsync(KeyGrabs(modifiers, keystrokes));

        public Task<(Dictionary<string, VariantValue> Data, double X, double Y)> QueryPointerAsync() =>
            Connection.CallMethodAsync(Message(A11yManagerObject.PointerInterface, "QueryPointer"), static (m, _) =>
            {
                var body = m.GetBodyReader();
                var data = body.ReadDictionaryOfStringToVariantValue();
                return (data, body.ReadDouble(), body.ReadDouble());
            }, null);

        public void Dispose() => Connection.Dispose();

        private MessageBuffer Message(string @interface, string member)
        {
            using var writer = Connection.GetMessageWriter();
            writer.WriteMethodCallHeader(A11yManager.BusName, A11yManager.ObjectPath, @interface, member);
            return writer.CreateMessage();
        }

        private MessageBuffer KeyGrabs(uint[] modifiers, (uint Keysym, uint Mask)[] keystrokes)
        {
            using var writer = Connection.GetMessageWriter();
            writer.WriteMethodCallHeader(A11yManager.BusName, A11yManager.ObjectPath, A11yManagerObject.KeyboardInterface, "SetKeyGrabs", "aua(uu)");
            writer.WriteArray(modifiers);
            var array = writer.WriteArrayStart(DBusType.Struct);
            foreach (var (keysym, mask) in keystrokes)
            {
                writer.WriteStructureStart();
                writer.WriteUInt32(keysym);
                writer.WriteUInt32(mask);
            }

            writer.WriteArrayEnd(array);
            return writer.CreateMessage();
        }
    }

    private static A11yManager Start(CompositorTestHost host, PrivateBus daemon, LayoutPointer pointer)
    {
        var manager = A11yManager.TryStart(host.Loop, host.Seat, pointer, new A11yManagerOptions { Address = daemon.Address }, out var reason);
        Assert.True(manager is not null, reason);
        Assert.Same(manager, host.Seat.KeyMonitor);
        return manager;
    }

    private static bool Blocked(CompositorTestHost host, uint time, uint key, bool pressed) => host.Seat.Keyboard.IsKeyBlocked(time, key, pressed);

    [Fact]
    public void A_watcher_hears_every_key_and_a_grab_holds_back_the_reader_shortcut()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        var pointer = new LayoutPointer(host.Layout);
        using var manager = Start(host, daemon, pointer);
        using var reader = PortalBusTests.Await(host, Reader.ConnectAsync(daemon.Address));

        PortalBusTests.Await(host, reader.CallAsync(A11yManagerObject.KeyboardInterface, "WatchKeyboard"));
        PortalBusTests.Await(host, reader.SetKeyGrabsAsync([SymInsert], [(SymH, 0)]));
        Assert.Equal(1, manager.KeyboardClients);

        Assert.False(Blocked(host, 100, KeyA, true));
        Assert.False(Blocked(host, 110, KeyA, false));
        Assert.True(Blocked(host, 1000, KeyInsert, true));
        Assert.True(Blocked(host, 1010, KeyH, true));
        Assert.True(Blocked(host, 1020, KeyH, false));
        Assert.True(Blocked(host, 1030, KeyInsert, false));
        Assert.True(Blocked(host, 3000, KeyH, true));
        Assert.True(Blocked(host, 3010, KeyH, false));

        PortalBusTests.PumpUntil(host, () => reader.Signals().Count >= 8);
        var signals = reader.Signals();
        Assert.Equal($"KeyEvent down 61 {KeyA + 8} 0", signals[0]);
        Assert.Contains($"KeyEvent down ff63 {KeyInsert + 8} 0", signals);
        Assert.Contains($"KeyEvent up 68 {KeyH + 8} 0", signals);
    }

    [Fact]
    public void A_double_press_of_the_reader_modifier_reaches_the_seat_and_a_vanished_reader_releases_its_grabs()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        var pointer = new LayoutPointer(host.Layout);
        using var manager = Start(host, daemon, pointer);
        var reader = PortalBusTests.Await(host, Reader.ConnectAsync(daemon.Address));
        PortalBusTests.Await(host, reader.SetKeyGrabsAsync([SymInsert], []));

        Assert.True(Blocked(host, 1000, KeyInsert, true));
        Assert.True(Blocked(host, 1050, KeyInsert, false));
        Assert.False(Blocked(host, 1200, KeyInsert, true));
        Assert.False(Blocked(host, 1250, KeyInsert, false));

        reader.Dispose();
        PortalBusTests.PumpUntil(host, () => manager.KeyboardClients == 0);
        Assert.False(Blocked(host, 9000, KeyInsert, true));
        Assert.False(Blocked(host, 9010, KeyInsert, false));
    }

    [Fact]
    public void QueryPointer_names_the_client_under_the_pointer_and_motion_signals_once()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        var pointer = new LayoutPointer(host.Layout);
        using var manager = Start(host, daemon, pointer);
        using var reader = PortalBusTests.Await(host, Reader.ConnectAsync(daemon.Address));
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 4, 5);

        var (data, x, y) = PortalBusTests.Await(host, reader.QueryPointerAsync());
        Assert.Equal(Environment.ProcessId, data["pid"].GetInt32());
        Assert.Equal((4.0, 5.0), (x, y));

        pointer.Motion(3, 0);
        pointer.Motion(3, 0);
        PortalBusTests.PumpUntil(host, () => reader.Signals().Contains("PointerPositionChanged"));
        Assert.Single(reader.Signals(), s => s == "PointerPositionChanged");
    }

    [Fact]
    public void Without_a_session_bus_the_manager_does_not_start_and_says_why()
    {
        using var host = new CompositorTestHost();
        var manager = A11yManager.TryStart(host.Loop, host.Seat, new LayoutPointer(host.Layout), new A11yManagerOptions { Address = "unix:path=/nonexistent/basin-a11y" }, out var reason);
        Assert.Null(manager);
        Assert.Contains("not reachable", reason, StringComparison.Ordinal);
        Assert.Null(host.Seat.KeyMonitor);
    }
}
