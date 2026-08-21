using Basin.Capabilities;
using System.Runtime.InteropServices;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ScreencopyTests
{
    [DllImport("libc")]
    private static extern int pipe(Span<int> fds);

    [DllImport("libc")]
    private static extern int close(int fd);

    private static (CompositorTestHost Host, ScreencopyManager Manager) HostWithCapture()
    {
        var host = new CompositorTestHost();
        var manager = new ScreencopyManager(host.Display, host.Layout, host.Buffers, new TestScreenCapture(host));
        return (host, manager);
    }

    private static Basin.Desktop.Protocol.ZwlrScreencopyManagerV1 Bind(CompositorTestHost host, ShmTestClient client)
    {
        Basin.Desktop.Protocol.ZwlrScreencopyManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_screencopy_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwlrScreencopyManagerV1>(e.Name, 3);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    [Fact]
    public void Capture_output_copies_the_scene()
    {
        var (host, manager) = HostWithCapture();
        using var _ = host;
        using var __ = manager;

        var rect = new Basin.Scene.SceneRect(host.Scene.Root, 40, 30, new RenderColor(1, 0, 0, 1));
        rect.SetPosition(10, 20);

        var proxy = Bind(host, host.Client);
        var frame = proxy.CaptureOutput(0, host.Client.Outputs[0]);
        var announced = (W: 0, H: 0, Stride: 0);
        var ready = false;
        var failed = false;
        frame.Buffer += (_, e) => announced = ((int)e.Width, (int)e.Height, (int)e.Stride);
        frame.Ready += (_, _) => ready = true;
        frame.Failed += (_, _) => failed = true;
        host.PumpUntil(() => announced.W != 0);
        Assert.Equal((160, 120, 640), announced);

        var target = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0x00000000));
        frame.Copy(target.Proxy);
        host.PumpUntil(() => ready || failed);
        Assert.True(ready);

        unsafe
        {
            var pixel = *(uint*)(target.Data + 25 * target.Stride + 15 * 4);
            Assert.Equal(0xFFFF0000u, pixel | 0xFF000000u);
            var outside = *(uint*)(target.Data + 100 * target.Stride + 100 * 4);
            Assert.Equal(0xFF000000u, outside | 0xFF000000u);
        }

        frame.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Region_capture_is_physical_pixels_at_fractional_scale()
    {
        var (host, manager) = HostWithCapture();
        using var _ = host;
        using var __ = manager;

        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetScale(2)));

        var rect = new Basin.Scene.SceneRect(host.Scene.Root, 40, 30, new RenderColor(1, 0, 0, 1));
        rect.SetPosition(10, 20);

        var proxy = Bind(host, host.Client);
        var frame = proxy.CaptureOutputRegion(0, host.Client.Outputs[0], 10, 20, 50, 40);
        var announced = (W: 0, H: 0);
        var ready = false;
        var failed = false;
        frame.Buffer += (_, e) => announced = ((int)e.Width, (int)e.Height);
        frame.Ready += (_, _) => ready = true;
        frame.Failed += (_, _) => failed = true;
        host.PumpUntil(() => announced.W != 0);
        Assert.Equal((100, 80), announced);

        var target = host.Client.CreateBuffer(100, 80, Fill.Solid(100, 80, 0x00000000));
        frame.Copy(target.Proxy);
        host.PumpUntil(() => ready || failed);
        Assert.True(ready);

        unsafe
        {
            var inside = *(uint*)(target.Data + 10 * target.Stride + 10 * 4);
            Assert.Equal(0xFFFF0000u, inside | 0xFF000000u);
            var outside = *(uint*)(target.Data + 70 * target.Stride + 90 * 4);
            Assert.Equal(0xFF000000u, outside | 0xFF000000u);
        }

        frame.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Copy_with_damage_completes_first_frame_then_waits_for_damage()
    {
        var (host, manager) = HostWithCapture();
        using var _ = host;
        using var __ = manager;

        var proxy = Bind(host, host.Client);

        var first = proxy.CaptureOutput(0, host.Client.Outputs[0]);
        var firstReady = false;
        Box firstReported = default;
        first.Ready += (_, _) => firstReady = true;
        first.Damage += (_, e) => firstReported = new Box((int)e.X, (int)e.Y, (int)e.Width, (int)e.Height);
        host.PumpToClient();
        var firstTarget = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0x00000000));
        first.CopyWithDamage(firstTarget.Proxy);
        host.PumpUntil(() => firstReady);
        Assert.Equal(new Box(0, 0, 160, 120), firstReported);
        first.Dispose();

        var frame = proxy.CaptureOutput(0, host.Client.Outputs[0]);
        var ready = false;
        Box reported = default;
        frame.Ready += (_, _) => ready = true;
        frame.Damage += (_, e) => reported = new Box((int)e.X, (int)e.Y, (int)e.Width, (int)e.Height);
        host.PumpToClient();

        var target = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0x00000000));
        frame.CopyWithDamage(target.Proxy);
        host.PumpToClient();
        Assert.False(ready);

        manager.NotifyOutputDamaged(host.Output, new Box(5, 6, 20, 10));
        host.PumpUntil(() => ready);
        Assert.Equal(new Box(5, 6, 20, 10), reported);

        manager.NotifyOutputDamaged(host.Output, new Box(1, 2, 8, 4));
        var third = proxy.CaptureOutput(0, host.Client.Outputs[0]);
        var thirdReady = false;
        Box thirdReported = default;
        third.Ready += (_, _) => thirdReady = true;
        third.Damage += (_, e) => thirdReported = new Box((int)e.X, (int)e.Y, (int)e.Width, (int)e.Height);
        host.PumpToClient();
        var thirdTarget = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0x00000000));
        third.CopyWithDamage(thirdTarget.Proxy);
        host.PumpUntil(() => thirdReady);
        Assert.Equal(new Box(1, 2, 8, 4), thirdReported);
        third.Dispose();

        frame.Dispose();
        host.PumpToServer();
    }
}

public sealed class DataControlTests
{
    [DllImport("libc")]
    private static extern unsafe int pipe(int* fds);

    [DllImport("libc")]
    private static extern int close(int fd);

    [DllImport("libc")]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

    private static Basin.Desktop.Protocol.ZwlrDataControlManagerV1 Bind(CompositorTestHost host, ShmTestClient client)
    {
        Basin.Desktop.Protocol.ZwlrDataControlManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_data_control_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwlrDataControlManagerV1>(e.Name, 2);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    [Fact]
    public unsafe void Manager_sets_clipboard_and_other_managers_read_it()
    {
        using var host = new CompositorTestHost();
        using var manager = new DataControlManager(host.Display, new Basin.Seat.SeatSelectionStore(host.Seat));

        var writer = host.ConnectClient();
        var writerProxy = Bind(host, writer);
        var source = writerProxy.CreateDataSource();
        var sendRequests = new List<(string Mime, int Fd)>();
        source.Send += (_, e) => sendRequests.Add((e.MimeType, e.Fd));
        source.Offer("text/plain");
        var writerDevice = writerProxy.GetDataDevice(writer.Seat!);
        writerDevice.SetSelection(source);
        host.PumpToServer();
        Assert.NotNull(host.Seat.DataDevice.Selection);
        Assert.Contains("text/plain", host.Seat.DataDevice.Selection!.MimeTypes);

        var reader = host.ConnectClient();
        var readerProxy = Bind(host, reader);
        Basin.Desktop.Protocol.ZwlrDataControlOfferV1? offer = null;
        var offeredMimes = new List<string>();
        var readerDevice = readerProxy.GetDataDevice(reader.Seat!);
        readerDevice.DataOffer += (_, e) =>
        {
            offer = e.Id;
            e.Id.Offer += (_, oe) => offeredMimes.Add(oe.MimeType);
        };
        readerDevice.Selection += (_, _) => { };
        host.PumpUntil(() => offer is not null && offeredMimes.Contains("text/plain"));

        var fds = stackalloc int[2];
        Assert.Equal(0, pipe(fds));
        offer!.Receive("text/plain", fds[1]);
        close(fds[1]);
        host.PumpUntil(() => sendRequests.Count > 0);
        Assert.Equal("text/plain", sendRequests[0].Mime);

        var payload = "basin-clipboard"u8;
        fixed (byte* p = payload)
        {
            write(sendRequests[0].Fd, p, (nuint)payload.Length);
        }

        close(sendRequests[0].Fd);

        Span<byte> incoming = stackalloc byte[64];
        nint got;
        fixed (byte* p = incoming)
        {
            got = read(fds[0], p, 64);
        }

        close(fds[0]);
        Assert.Equal("basin-clipboard", System.Text.Encoding.UTF8.GetString(incoming[..(int)got]));
    }

    [Fact]
    public void Primary_selection_channel_is_independent()
    {
        using var host = new CompositorTestHost();
        using var manager = new DataControlManager(host.Display, new Basin.Seat.SeatSelectionStore(host.Seat));
        using var primary = new PrimarySelectionManager(host.Display, new Basin.Seat.SeatSelectionStore(host.Seat), host.Seat);

        var client = host.ConnectClient();
        var proxy = Bind(host, client);
        var source = proxy.CreateDataSource();
        source.Offer("text/plain");
        var device = proxy.GetDataDevice(client.Seat!);
        device.SetPrimarySelection(source);
        host.PumpToServer();

        Assert.NotNull(host.Seat.DataDevice.PrimarySelection);
        Assert.Null(host.Seat.DataDevice.Selection);
    }

    [Fact]
    public void Primary_selection_offers_wait_for_keyboard_focus()
    {
        using var host = new CompositorTestHost();
        using var primary = new PrimarySelectionManager(host.Display, new Basin.Seat.SeatSelectionStore(host.Seat), host.Seat);

        var writer = host.ConnectClient();
        var writerProxy = BindPrimary(host, writer);
        var source = writerProxy.CreateSource();
        source.Offer("text/plain");
        writerProxy.GetDevice(writer.Seat!).SetSelection(source, 0);
        host.PumpToServer();
        Assert.NotNull(host.Seat.DataDevice.PrimarySelection);

        var reader = host.ConnectClient();
        var window = MappedToplevel.Map(host, reader);
        var readerDevice = BindPrimary(host, reader).GetDevice(reader.Seat!);
        var selectionEvents = 0;
        var mimes = new List<string>();
        readerDevice.DataOffer += (_, e) => e.Offer.Offer += (_, oe) => mimes.Add(oe.MimeType);
        readerDevice.Selection += (_, _) => selectionEvents++;
        host.PumpToClient();
        Assert.Equal(0, selectionEvents);

        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpUntil(() => selectionEvents == 1 && mimes.Contains("text/plain"));
    }

    private static Basin.Desktop.Protocol.ZwpPrimarySelectionDeviceManagerV1 BindPrimary(CompositorTestHost host, ShmTestClient client)
    {
        Basin.Desktop.Protocol.ZwpPrimarySelectionDeviceManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_primary_selection_device_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwpPrimarySelectionDeviceManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}

public sealed class OutputManagementTests
{
    [Fact]
    public void Heads_announce_and_apply_moves_the_layout()
    {
        using var host = new CompositorTestHost();
        using var manager = new OutputManagementManager(
            host.Display,
            host.Layout,
            new Basin.Capabilities.Defaults.LayoutOutputSet(host.Layout),
            new Basin.Capabilities.Defaults.LayoutOutputConfiguration(host.Layout));

        var client = host.ConnectClient();
        Basin.Desktop.Protocol.ZwlrOutputManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_output_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwlrOutputManagerV1>(e.Name, 2);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        Basin.Desktop.Protocol.ZwlrOutputHeadV1? head = null;
        var headName = "";
        uint serial = 0;
        var position = (X: -1, Y: -1);
        proxy!.Head += (_, e) =>
        {
            head = e.Head;
            e.Head.Name += (_, ne) => headName = ne.Name;
            e.Head.Position += (_, pe) => position = (pe.X, pe.Y);
        };
        proxy.Done += (_, e) => serial = e.Serial;
        host.PumpUntil(() => serial != 0 && head is not null);
        Assert.Equal(host.Output.Name, headName);
        Assert.Equal((0, 0), position);

        var configuration = proxy.CreateConfiguration(serial);
        var configurationHead = configuration.EnableHead(head!);
        configurationHead.SetPosition(500, 300);
        var succeeded = false;
        var failed = false;
        configuration.Succeeded += (_, _) => succeeded = true;
        configuration.Failed += (_, _) => failed = true;
        configuration.Apply();
        host.PumpUntil(() => succeeded || failed);
        Assert.True(succeeded);
        Assert.Equal(new Box(500, 300, 160, 120), host.Layout.BoxOf(host.Output));
    }

    [Fact]
    public void A_disabled_head_leaves_the_layout_and_comes_back_where_it_was()
    {
        using var host = new CompositorTestHost();
        using var manager = new OutputManagementManager(
            host.Display,
            host.Layout,
            new Basin.Capabilities.Defaults.LayoutOutputSet(host.Layout),
            new Basin.Capabilities.Defaults.LayoutOutputConfiguration(host.Layout));

        host.Layout.Move(host.Output, 200, 100);
        uint serial = 0;
        Basin.Desktop.Protocol.ZwlrOutputHeadV1? head = null;
        var enabled = new List<int>();
        var proxy = BindManager(host, 4, p =>
        {
            p.Head += (_, e) =>
            {
                head ??= e.Head;
                e.Head.Enabled += (_, en) => enabled.Add(en.Enabled);
            };
            p.Done += (_, e) => serial = e.Serial;
        });
        host.PumpUntil(() => serial != 0 && head is not null);
        Assert.Equal(1, enabled[^1]);

        var off = proxy.CreateConfiguration(serial);
        var offApplied = false;
        off.Succeeded += (_, _) => offApplied = true;
        off.DisableHead(head!);
        off.Apply();
        host.PumpUntil(() => offApplied);

        Assert.False(host.Layout.Contains(host.Output));
        host.PumpToClient();
        Assert.Equal(0, enabled[^1]);
        Assert.False(head!.IsDestroyed);

        var on = proxy.CreateConfiguration(serial);
        var onApplied = false;
        on.Succeeded += (_, _) => onApplied = true;
        on.EnableHead(head!);
        on.Apply();
        host.PumpUntil(() => onApplied);

        Assert.True(host.Layout.Contains(host.Output));
        Assert.Equal(new Box(200, 100, 160, 120), host.Layout.BoxOf(host.Output));
        host.PumpToClient();
        Assert.Equal(1, enabled[^1]);
    }

    [Fact]
    public void A_powered_off_output_keeps_its_head_and_stays_off_when_a_client_configures_it()
    {
        using var host = new CompositorTestHost();
        var configuration = new Basin.Capabilities.Defaults.LayoutOutputConfiguration(host.Layout);
        using var manager = new OutputManagementManager(
            host.Display, host.Layout, new Basin.Capabilities.Defaults.LayoutOutputSet(host.Layout), configuration);

        using (var off = new OutputState())
        {
            Assert.True(host.Output.Commit(off.SetEnabled(false)));
        }

        uint serial = 0;
        Basin.Desktop.Protocol.ZwlrOutputHeadV1? head = null;
        var enabled = new List<int>();
        var proxy = BindManager(host, 4, p =>
        {
            p.Head += (_, e) =>
            {
                head ??= e.Head;
                e.Head.Enabled += (_, en) => enabled.Add(en.Enabled);
            };
            p.Done += (_, e) => serial = e.Serial;
        });
        host.PumpUntil(() => serial != 0 && head is not null);
        Assert.Equal(1, enabled[^1]);

        var config = proxy.CreateConfiguration(serial);
        var applied = false;
        config.Succeeded += (_, _) => applied = true;
        config.EnableHead(head!).SetScale(WlFixed.FromDouble(2));
        config.Apply();
        host.PumpUntil(() => applied);

        Assert.False(host.Output.Enabled);
        Assert.True(host.Layout.Contains(host.Output));
        Assert.Equal(2, host.Output.Scale);
    }

    [Fact]
    public void A_hotplugged_output_reaches_a_manager_that_is_already_bound()
    {
        using var host = new CompositorTestHost();
        using var manager = new OutputManagementManager(
            host.Display,
            host.Layout,
            new Basin.Capabilities.Defaults.LayoutOutputSet(host.Layout),
            new Basin.Capabilities.Defaults.LayoutOutputConfiguration(host.Layout));

        uint serial = 0;
        var names = new List<string>();
        _ = BindManager(host, 4, p =>
        {
            p.Head += (_, e) => e.Head.Name += (_, ne) => names.Add(ne.Name);
            p.Done += (_, e) => serial = e.Serial;
        });
        host.PumpUntil(() => serial != 0 && names.Count == 1);

        var hotplug = host.Backend.CreateOutput(new OutputMode(64, 48, 60_000), manualFrameClock: true);
        host.Layout.Add(hotplug, 0, 0);
        host.PumpUntil(() => names.Count == 2);

        Assert.Equal([host.Output.Name, hotplug.Name], names);
        hotplug.Destroy();
    }

    [Fact]
    public void Adaptive_sync_reaches_the_configuration_and_is_gated_on_version()
    {
        using var host = new CompositorTestHost();
        var configuration = new RecordingOutputConfiguration();
        using var manager = new OutputManagementManager(
            host.Display, host.Layout, new Basin.Capabilities.Defaults.LayoutOutputSet(host.Layout), configuration);

        var modernStates = new List<uint>();
        var legacyStates = new List<uint>();
        uint serial = 0;
        Basin.Desktop.Protocol.ZwlrOutputHeadV1? head = null;

        var modern = BindManager(host, 4, proxy =>
        {
            proxy.Head += (_, e) =>
            {
                head ??= e.Head;
                e.Head.AdaptiveSync += (_, ae) => modernStates.Add((uint)ae.State);
            };
            proxy.Done += (_, e) => serial = e.Serial;
        });
        _ = BindManager(host, 2, proxy =>
            proxy.Head += (_, e) => e.Head.AdaptiveSync += (_, ae) => legacyStates.Add((uint)ae.State));
        host.PumpUntil(() => serial != 0 && head is not null);

        Assert.Equal(0u, Assert.Single(modernStates));
        Assert.Empty(legacyStates);

        var config = modern.CreateConfiguration(serial);
        var succeeded = false;
        config.Succeeded += (_, _) => succeeded = true;
        var configHead = config.EnableHead(head!);
        configHead.SetAdaptiveSync(Basin.Desktop.Protocol.ZwlrOutputHeadV1.AdaptiveSyncState.Enabled);
        config.Apply();
        host.PumpUntil(() => succeeded);

        Assert.True(succeeded);
        Assert.True(Assert.Single(configuration.Entries).AdaptiveSync);
    }

    [Fact]
    public void Adaptive_sync_a_backend_cannot_do_fails_rather_than_killing_the_client()
    {
        using var host = new CompositorTestHost();
        using var manager = new OutputManagementManager(
            host.Display,
            host.Layout,
            new Basin.Capabilities.Defaults.LayoutOutputSet(host.Layout),
            new Basin.Capabilities.Defaults.LayoutOutputConfiguration(host.Layout));

        uint serial = 0;
        Basin.Desktop.Protocol.ZwlrOutputHeadV1? head = null;
        var proxy = BindManager(host, 4, p =>
        {
            p.Head += (_, e) => head ??= e.Head;
            p.Done += (_, e) => serial = e.Serial;
        });
        host.PumpUntil(() => serial != 0 && head is not null);

        var config = proxy.CreateConfiguration(serial);
        var failed = false;
        config.Failed += (_, _) => failed = true;
        var configHead = config.EnableHead(head!);
        configHead.SetAdaptiveSync(Basin.Desktop.Protocol.ZwlrOutputHeadV1.AdaptiveSyncState.Enabled);
        config.Apply();
        host.PumpUntil(() => failed);

        Assert.True(failed);
        Assert.False(host.Output.AdaptiveSync);
        host.PumpToServer();
        Assert.False(host.Client.Display.IsDestroyed);
    }

    private static Basin.Desktop.Protocol.ZwlrOutputManagerV1 BindManager(
        CompositorTestHost host,
        uint version,
        Action<Basin.Desktop.Protocol.ZwlrOutputManagerV1> wire)
    {
        Basin.Desktop.Protocol.ZwlrOutputManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_output_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwlrOutputManagerV1>(e.Name, version);

                wire(proxy);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    private sealed class RecordingOutputConfiguration : IOutputConfiguration
    {
        public List<OutputConfigurationEntry> Entries { get; } = [];

        public event Action<IReadOnlyList<OutputConfigurationEntry>>? Applied;

        public bool Test(IReadOnlyList<OutputConfigurationEntry> entries) => true;

        public bool Apply(IReadOnlyList<OutputConfigurationEntry> entries)
        {
            Entries.AddRange(entries);
            Applied?.Invoke(entries);
            return true;
        }
    }
}

public sealed class PointerConstraintTests
{
    [Fact]
    public void Lock_activates_and_relative_motion_flows()
    {
        using var host = new CompositorTestHost();
        using var constraints = new PointerConstraintsManager(host.Display, host.Compositor);
        using var relative = new RelativePointerManager(host.Display, host.Seat);

        PointerConstraint? serverConstraint = null;
        constraints.ConstraintCreated += c => serverConstraint = c;

        var client = host.Client;
        Basin.Desktop.Protocol.ZwpPointerConstraintsV1? constraintsProxy = null;
        Basin.Desktop.Protocol.ZwpRelativePointerManagerV1? relativeProxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_pointer_constraints_v1")
            {
                constraintsProxy = registry.Bind<Basin.Desktop.Protocol.ZwpPointerConstraintsV1>(e.Name, 1);
            }
            else if (e.Interface == "zwp_relative_pointer_manager_v1")
            {
                relativeProxy = registry.Bind<Basin.Desktop.Protocol.ZwpRelativePointerManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF224466));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        var pointer = client.Seat!.GetPointer();
        var relativePointer = relativeProxy!.GetRelativePointer(pointer);
        var deltas = new List<(double Dx, double DyUnaccel)>();
        relativePointer.RelativeMotion += (_, e) => deltas.Add((e.Dx.ToDouble(), e.DyUnaccel.ToDouble()));

        var locked = false;
        var unlocked = false;
        var lockProxy = constraintsProxy!.LockPointer(surface, pointer, null, Basin.Desktop.Protocol.ZwpPointerConstraintsV1.Lifetime.Persistent);
        lockProxy.Locked += (_, _) => locked = true;
        lockProxy.Unlocked += (_, _) => unlocked = true;
        host.PumpUntil(() => serverConstraint is not null);

        var serverSurface = host.SurfaceScenes[0].Surface;
        host.Seat.Pointer.NotifyEnter(serverSurface, 5, 5);
        serverConstraint!.Activate();
        host.PumpUntil(() => locked);

        relative.NotifyMotion(123_456, 3.5, -2.0, 4.0, -2.5);
        host.PumpUntil(() => deltas.Count > 0);
        Assert.Equal(3.5, deltas[0].Dx, 2);
        Assert.Equal(-2.5, deltas[0].DyUnaccel, 2);

        serverConstraint.Deactivate();
        host.PumpUntil(() => unlocked);
        Assert.True(serverConstraint.IsActive == false);

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void The_cursor_position_hint_is_double_buffered_and_survives_the_unlock()
    {
        using var host = new CompositorTestHost();
        using var constraints = new PointerConstraintsManager(host.Display, host.Compositor);

        PointerConstraint? serverConstraint = null;
        constraints.ConstraintCreated += c => serverConstraint = c;

        var client = host.Client;
        Basin.Desktop.Protocol.ZwpPointerConstraintsV1? constraintsProxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_pointer_constraints_v1")
            {
                constraintsProxy = registry.Bind<Basin.Desktop.Protocol.ZwpPointerConstraintsV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF224466));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        var pointer = client.Seat!.GetPointer();
        var lockProxy = constraintsProxy!.LockPointer(
            surface, pointer, null, Basin.Desktop.Protocol.ZwpPointerConstraintsV1.Lifetime.Persistent);
        host.PumpUntil(() => serverConstraint is not null);

        Assert.Null(serverConstraint!.CursorPositionHint);

        lockProxy.SetCursorPositionHint(WlFixed.FromDouble(12.5), WlFixed.FromDouble(7.25));
        host.PumpToServer();
        Assert.Null(serverConstraint.CursorPositionHint);

        surface.Commit();
        host.PumpToServer();
        Assert.Equal((12.5, 7.25), serverConstraint.CursorPositionHint);

        lockProxy.SetCursorPositionHint(WlFixed.FromDouble(30), WlFixed.FromDouble(40));
        host.PumpToServer();
        Assert.Equal((12.5, 7.25), serverConstraint.CursorPositionHint);
        surface.Commit();
        host.PumpToServer();
        Assert.Equal((30.0, 40.0), serverConstraint.CursorPositionHint);

        var serverSurface = host.SurfaceScenes[0].Surface;
        host.Seat.Pointer.NotifyEnter(serverSurface, 5, 5);
        serverConstraint.Activate();
        host.PumpToServer();
        serverConstraint.Deactivate();
        host.PumpToServer();
        Assert.Equal((30.0, 40.0), serverConstraint.CursorPositionHint);

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Destroying_the_lock_object_leaves_no_constraint_still_claiming_to_be_active()
    {
        using var host = new CompositorTestHost();
        using var constraints = new PointerConstraintsManager(host.Display, host.Compositor);

        PointerConstraint? serverConstraint = null;
        constraints.ConstraintCreated += c => serverConstraint = c;

        var client = host.Client;
        Basin.Desktop.Protocol.ZwpPointerConstraintsV1? constraintsProxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_pointer_constraints_v1")
            {
                constraintsProxy = registry.Bind<Basin.Desktop.Protocol.ZwpPointerConstraintsV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF224466));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        var pointer = client.Seat!.GetPointer();
        var lockProxy = constraintsProxy!.LockPointer(
            surface, pointer, null, Basin.Desktop.Protocol.ZwpPointerConstraintsV1.Lifetime.Persistent);
        host.PumpUntil(() => serverConstraint is not null);

        var deactivated = 0;
        serverConstraint!.Deactivated += () => deactivated++;
        host.Seat.Pointer.NotifyEnter(host.SurfaceScenes[0].Surface, 5, 5);
        serverConstraint.Activate();
        host.PumpToServer();
        Assert.True(serverConstraint.IsActive);

        lockProxy.Destroy();
        host.PumpToServer();

        Assert.False(serverConstraint.IsActive);
        Assert.Equal(1, deactivated);
        Assert.Null(constraints.ConstraintFor(host.SurfaceScenes[0].Surface));

        surface.Dispose();
        host.PumpToServer();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Deactivating_reports_once_however_the_constraint_ends(bool persistent)
    {
        using var host = new CompositorTestHost();
        using var constraints = new PointerConstraintsManager(host.Display, host.Compositor);

        PointerConstraint? serverConstraint = null;
        constraints.ConstraintCreated += c => serverConstraint = c;

        var client = host.Client;
        Basin.Desktop.Protocol.ZwpPointerConstraintsV1? constraintsProxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_pointer_constraints_v1")
            {
                constraintsProxy = registry.Bind<Basin.Desktop.Protocol.ZwpPointerConstraintsV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF224466));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        var pointer = client.Seat!.GetPointer();
        var lockProxy = constraintsProxy!.LockPointer(
            surface,
            pointer,
            null,
            persistent
                ? Basin.Desktop.Protocol.ZwpPointerConstraintsV1.Lifetime.Persistent
                : Basin.Desktop.Protocol.ZwpPointerConstraintsV1.Lifetime.Oneshot);
        host.PumpUntil(() => serverConstraint is not null);

        var deactivated = 0;
        serverConstraint!.Deactivated += () => deactivated++;
        host.Seat.Pointer.NotifyEnter(host.SurfaceScenes[0].Surface, 5, 5);
        serverConstraint.Activate();
        host.PumpToServer();

        serverConstraint.Deactivate();
        host.PumpToServer();
        Assert.Equal(1, deactivated);
        Assert.False(serverConstraint.IsActive);

        lockProxy.Destroy();
        host.PumpToServer();
        Assert.Equal(1, deactivated);

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_destroyed_surface_ends_the_constraint_it_carried()
    {
        using var host = new CompositorTestHost();
        using var constraints = new PointerConstraintsManager(host.Display, host.Compositor);

        PointerConstraint? serverConstraint = null;
        constraints.ConstraintCreated += c => serverConstraint = c;

        var client = host.Client;
        Basin.Desktop.Protocol.ZwpPointerConstraintsV1? constraintsProxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_pointer_constraints_v1")
            {
                constraintsProxy = registry.Bind<Basin.Desktop.Protocol.ZwpPointerConstraintsV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF224466));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        var pointer = client.Seat!.GetPointer();
        _ = constraintsProxy!.ConfinePointer(
            surface, pointer, null, Basin.Desktop.Protocol.ZwpPointerConstraintsV1.Lifetime.Persistent);
        host.PumpUntil(() => serverConstraint is not null);

        var deactivated = 0;
        serverConstraint!.Deactivated += () => deactivated++;
        host.Seat.Pointer.NotifyEnter(host.SurfaceScenes[0].Surface, 5, 5);
        serverConstraint.Activate();
        host.PumpToServer();
        Assert.True(serverConstraint.IsActive);

        surface.Dispose();
        host.PumpToServer();
        Assert.False(serverConstraint.IsActive);
        Assert.Equal(1, deactivated);
    }

    [Fact]
    public void Disposing_the_manager_ends_the_constraints_it_still_holds()
    {
        using var host = new CompositorTestHost();
        var constraints = new PointerConstraintsManager(host.Display, host.Compositor);

        PointerConstraint? serverConstraint = null;
        constraints.ConstraintCreated += c => serverConstraint = c;

        var client = host.Client;
        Basin.Desktop.Protocol.ZwpPointerConstraintsV1? constraintsProxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_pointer_constraints_v1")
            {
                constraintsProxy = registry.Bind<Basin.Desktop.Protocol.ZwpPointerConstraintsV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF224466));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        var pointer = client.Seat!.GetPointer();
        _ = constraintsProxy!.LockPointer(
            surface, pointer, null, Basin.Desktop.Protocol.ZwpPointerConstraintsV1.Lifetime.Persistent);
        host.PumpUntil(() => serverConstraint is not null);

        var deactivated = 0;
        serverConstraint!.Deactivated += () => deactivated++;
        host.Seat.Pointer.NotifyEnter(host.SurfaceScenes[0].Surface, 5, 5);
        serverConstraint.Activate();
        host.PumpToServer();
        Assert.True(serverConstraint.IsActive);

        constraints.Dispose();
        host.PumpToServer();
        Assert.False(serverConstraint.IsActive);
        Assert.Equal(1, deactivated);

        surface.Dispose();
        host.PumpToServer();
        Assert.Equal(1, deactivated);
    }

    [Fact]
    public void A_confined_pointer_never_has_a_hint()
    {
        using var host = new CompositorTestHost();
        using var constraints = new PointerConstraintsManager(host.Display, host.Compositor);

        PointerConstraint? serverConstraint = null;
        constraints.ConstraintCreated += c => serverConstraint = c;

        var client = host.Client;
        Basin.Desktop.Protocol.ZwpPointerConstraintsV1? constraintsProxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_pointer_constraints_v1")
            {
                constraintsProxy = registry.Bind<Basin.Desktop.Protocol.ZwpPointerConstraintsV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF224466));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        var pointer = client.Seat!.GetPointer();
        _ = constraintsProxy!.ConfinePointer(
            surface, pointer, null, Basin.Desktop.Protocol.ZwpPointerConstraintsV1.Lifetime.Persistent);
        host.PumpUntil(() => serverConstraint is not null);

        surface.Commit();
        host.PumpToServer();
        Assert.Equal(ConstraintKind.Confine, serverConstraint!.Kind);
        Assert.Null(serverConstraint.CursorPositionHint);

        surface.Dispose();
        host.PumpToServer();
    }
}
