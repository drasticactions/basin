using Basin.Capabilities;
using Basin.Desktop;
using Xunit;

namespace Basin.Tests;

public sealed class HostOriginatedInputTests
{
    private sealed class RecordingMethod : ITextInputMethod
    {
        public bool IsAvailable => true;

        public bool HasKeyboardGrab => false;

        public List<Box> CursorRectangles { get; } = [];

        public List<string> Surroundings { get; } = [];

        public Surface? Active { get; private set; }

        public event Action<PreeditString>? Preedit;

        public event Action<string>? CommitString;

        public event Action<uint, uint>? DeleteSurroundingText;

        public event Action? Done;

        public event Action? AvailabilityChanged;

        public void Activate(Surface surface) => Active = surface;

        public void Deactivate(Surface surface) => Active = null;

        public void SurroundingText(string text, uint cursor, uint anchor) => Surroundings.Add(text);

        public void ContentType(uint hint, uint purpose)
        {
        }

        public void CursorRectangle(in Box rect) => CursorRectangles.Add(rect);

        public void Commit(uint serial)
        {
        }

        public void ForwardKey(uint timeMs, uint keycode, bool pressed)
        {
        }

        public void ForwardModifiers(uint depressed, uint latched, uint locked, uint group)
        {
        }

        public void Raise(string text)
        {
            CommitString?.Invoke(text);
            Done?.Invoke();
        }

        public void RaisePreedit(string text)
        {
            Preedit?.Invoke(new PreeditString(text, 0, 0));
            Done?.Invoke();
        }

        public void RaiseDelete(uint before, uint after)
        {
            DeleteSurroundingText?.Invoke(before, after);
            Done?.Invoke();
        }

        public void RaiseAvailability() => AvailabilityChanged?.Invoke();
    }

    [Fact]
    public void The_caret_a_client_sets_reaches_the_input_method()
    {
        using var host = new CompositorTestHost();
        var method = new RecordingMethod();
        using var manager = new TextInputManager(host.Display, host.Seat, method);
        var window = MappedToplevel.Map(host, host.Client);

        var textInputManager = Bind<Basin.Desktop.Protocol.ZwpTextInputManagerV3>(host, "zwp_text_input_manager_v3");
        var textInput = textInputManager.GetTextInput(host.Client.Seat!);
        host.PumpToServer();

        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        manager.NotifyFocus(window.ServerSurface);
        host.PumpToClient();

        textInput.Enable();
        textInput.SetCursorRectangle(12, 34, 2, 18);
        textInput.Commit();
        host.PumpUntil(() => method.CursorRectangles.Count > 0);

        var caret = method.CursorRectangles[^1];
        Assert.Equal(12, caret.X);
        Assert.Equal(34, caret.Y);
        Assert.Equal(2, caret.Width);
        Assert.Equal(18, caret.Height);

        textInput.Dispose();
        textInputManager.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_caret_that_moves_is_reported_again()
    {
        using var host = new CompositorTestHost();
        var method = new RecordingMethod();
        using var manager = new TextInputManager(host.Display, host.Seat, method);
        var window = MappedToplevel.Map(host, host.Client);

        var textInputManager = Bind<Basin.Desktop.Protocol.ZwpTextInputManagerV3>(host, "zwp_text_input_manager_v3");
        var textInput = textInputManager.GetTextInput(host.Client.Seat!);
        host.PumpToServer();

        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        manager.NotifyFocus(window.ServerSurface);
        host.PumpToClient();

        textInput.Enable();
        textInput.SetCursorRectangle(1, 2, 3, 4);
        textInput.Commit();
        host.PumpUntil(() => method.CursorRectangles.Count > 0);

        textInput.SetCursorRectangle(40, 50, 3, 20);
        textInput.Commit();
        host.PumpUntil(() => method.CursorRectangles[^1].X == 40);

        Assert.Equal(50, method.CursorRectangles[^1].Y);

        textInput.Dispose();
        textInputManager.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void The_compositor_can_start_a_drag_of_its_own()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        var device = host.Client.DataDeviceManager!.GetDataDevice(host.Client.Seat!);
        var offers = new List<Wayland.WlDataOffer>();
        var entered = 0;
        var offered = new List<string>();
        device.DataOffer += (_, e) =>
        {
            offers.Add(e.Id);
            e.Id.Offer += (_, mime) => offered.Add(mime.MimeType);
        };
        device.Enter += (_, _) => entered++;
        host.PumpToServer();

        host.Seat.Pointer.NotifyMotionAt(1, window.ServerSurface, 5, 5, 5, 5);
        host.PumpToClient();

        var sent = new List<string>();
        var source = new DataSource(
            ["text/plain;charset=utf-8"],
            (mime, fd) =>
            {
                sent.Add(mime);
                fd.Close();
            });

        Assert.True(host.Seat.DataDevice.StartDrag(source));
        Assert.Same(source, host.Seat.DataDevice.DraggingSource);

        host.PumpUntil(() => entered == 1 && offered.Count == 1);
        Assert.Equal("text/plain;charset=utf-8", offered[0]);

        foreach (var offer in offers)
        {
            offer.Dispose();
        }

        device.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_drag_the_compositor_started_ends_when_the_button_comes_up()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        host.PumpToServer();
        host.Seat.Pointer.NotifyMotionAt(1, window.ServerSurface, 5, 5, 5, 5);

        var ended = 0;
        host.Seat.DataDevice.DragEnded += () => ended++;

        var source = new DataSource(["text/plain"], (_, fd) => fd.Close());
        Assert.True(host.Seat.DataDevice.StartDrag(source));

        host.Seat.Pointer.NotifyButton(2, 0x110, pressed: false);
        host.PumpToServer();

        Assert.Equal(1, ended);
        Assert.Null(host.Seat.DataDevice.DraggingSource);
    }

    [Fact]
    public void A_client_that_answers_a_ping_is_reported_with_the_serial_it_answered()
    {
        using var host = new CompositorTestHost();
        MappedToplevel.Map(host, host.Client);

        var ponged = new List<uint>();
        host.Shell.Ponged += (_, serial) => ponged.Add(serial);
        host.Client.WmBase!.Ping += (_, e) => host.Client.WmBase!.Pong(e.Serial);
        host.PumpToServer();

        var client = Assert.Single(host.Shell.BoundClients);
        var sent = host.Shell.Ping(client);
        Assert.NotEqual(0u, sent);

        host.PumpUntil(() => ponged.Count == 1);
        Assert.Equal(sent, ponged[0]);
    }

    [Fact]
    public void A_client_that_never_answers_reports_nothing()
    {
        using var host = new CompositorTestHost();
        MappedToplevel.Map(host, host.Client);

        var ponged = 0;
        host.Shell.Ponged += (_, _) => ponged++;
        host.PumpToServer();

        var client = Assert.Single(host.Shell.BoundClients);
        Assert.NotEqual(0u, host.Shell.Ping(client));

        host.PumpToClient();
        host.PumpToClient();

        Assert.Equal(0, ponged);
    }

    [Fact]
    public void A_channel_carries_a_pipe_the_compositor_opened()
    {
        using var peer = new LoopbackChannel();

        var slot = peer.Channel.CreateWritablePipe();
        Assert.NotEqual(0, slot);

        var pipe = peer.Channel.Transport.Slots.Resolve<Basin.Transport.Waypipe.WaypipePipe>(slot);
        Assert.True(pipe.CanWrite);

        pipe.Write("hello"u8);
        pipe.CloseWrite();

        var frames = peer.ReadFrames();
        Assert.Contains(Basin.Transport.Waypipe.WaypipeMessageType.OpenIWPipe, frames.Select(f => f.Type));
        Assert.Contains(Basin.Transport.Waypipe.WaypipeMessageType.PipeShutdownW, frames.Select(f => f.Type));

        var transfer = frames.Single(f => f.Type == Basin.Transport.Waypipe.WaypipeMessageType.PipeTransfer);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(transfer.Body.AsSpan(4)));
        Assert.Equal(pipe.RemoteId, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(transfer.Body));
    }

    [Fact]
    public void A_pipe_the_peer_opened_toward_its_reader_is_ours_to_write()
    {
        using var peer = new LoopbackChannel();

        Span<byte> open = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open, 7);
        peer.Channel.Engine.Apply(Basin.Transport.Waypipe.WaypipeMessageType.OpenIRPipe, open);
        peer.Channel.Engine.Apply(Basin.Transport.Waypipe.WaypipeMessageType.InjectRIDs, open);

        var request = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(request, 4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), (8u << 16) | (1u << 11));
        peer.Channel.Engine.Apply(Basin.Transport.Waypipe.WaypipeMessageType.Protocol, request);

        var slots = new int[4];
        var (_, fds) = peer.Channel.Transport.TryReadNonBlocking(
            new byte[64], Array.Empty<byte>(), slots, Array.Empty<int>());
        Assert.Equal(1, fds);

        var pipe = Assert.IsAssignableFrom<IPipeToClient>(
            peer.Channel.Transport.Slots.Resolve<object>(slots[0]));
        Assert.True(pipe.CanWrite);
        pipe.Write("hello"u8);
        pipe.CloseWrite();

        var frames = peer.ReadFrames();
        var transfer = frames.Single(f => f.Type == Basin.Transport.Waypipe.WaypipeMessageType.PipeTransfer);
        Assert.Equal(7, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(transfer.Body));
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(transfer.Body.AsSpan(4)));
        Assert.Contains(frames, f => f.Type == Basin.Transport.Waypipe.WaypipeMessageType.PipeShutdownW
            && System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(f.Body) == 7);
    }

    private static T Bind<T>(CompositorTestHost host, string wireInterface)
        where T : Wayland.WlProxy, Wayland.IWaylandObject<T>
    {
        T? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == wireInterface)
            {
                proxy = registry.Bind<T>(e.Name, 1);
            }
        };

        host.PumpToClient();
        registry.Dispose();
        return proxy ?? throw new InvalidOperationException($"{wireInterface} was not advertised");
    }
}
