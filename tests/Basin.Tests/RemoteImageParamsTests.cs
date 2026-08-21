using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Basin.Protocol;
using Basin.Transport.Waypipe;
using Wayland.Server;
using Xunit;

namespace Basin.Tests;

public sealed class RemoteImageParamsTests
{
    private const uint RegistryId = 2;
    private const uint DmabufId = 3;
    private const uint CompositorId = 4;
    private const uint ParamsId = 5;
    private const uint BufferId = 6;
    private const uint SurfaceId = 7;

    internal sealed unsafe class FakeRemoteImage : IRemoteImage, IFdSlotPayload
    {
        private nint _data;

        public FakeRemoteImage(int width, int height, DrmFormat format)
        {
            Width = width;
            Height = height;
            Format = format;
            Stride = width * format.BytesPerPixel();
            _data = (nint)NativeMemory.AllocZeroed((nuint)(Stride * height));
            References = 1;
        }

        public int References { get; private set; }

        public int Width { get; }

        public int Height { get; }

        public DrmFormat Format { get; }

        public int Stride { get; }

        public nint Pixels => _data;

        public bool IsReleased => References == 0;

        public void AddRef() => References++;

        public void Release()
        {
            if (--References == 0)
            {
                NativeMemory.Free((void*)_data);
                _data = 0;
            }
        }
    }

    internal sealed class TokenClientHost : IDisposable
    {
        private readonly List<byte> _outbound = [];

        public TokenClientHost(DrmFormatSet formats)
        {
            BasinCounters.Reset();
            Display = WlServerDisplay.Create(new ManagedTransport());
            Loop = new WaylandEventLoop(Display);
            Buffers = new ClientBufferRegistry();
            Compositor = new CompositorGlobal(Display, Buffers);
            Compositor.SurfaceCreated += Surfaces.Add;
            Dmabuf = new LinuxDmabufGlobal(Display, Buffers, formats, 0x1234UL, compositor: Compositor);
            Transport = new WaypipeClientTransport();
            Transport.Outbound += (bytes, _) => _outbound.AddRange(bytes.ToArray());
            Display.CreateClient(Transport);
        }

        public WlServerDisplay Display { get; }

        public WaylandEventLoop Loop { get; }

        public ClientBufferRegistry Buffers { get; }

        public CompositorGlobal Compositor { get; }

        public LinuxDmabufGlobal Dmabuf { get; }

        public WaypipeClientTransport Transport { get; }

        public List<ManagedWire.WireMessage> Events { get; } = [];

        public List<Surface> Surfaces { get; } = [];

        public void Send(byte[] message, params int[] slots)
        {
            Transport.Deliver(message, slots);
            Pump();
        }

        public void Pump()
        {
            for (var i = 0; i < 3; i++)
            {
                Loop.Dispatch(0);
                Display.FlushClients();
            }

            var pending = _outbound.ToArray();
            var consumed = ManagedWire.ParseInto(Events, pending, pending.Length);
            _outbound.RemoveRange(0, consumed);
        }

        public void BindGlobals(uint dmabufVersion = 4)
        {
            Send(ManagedWire.Request(1, 1, RegistryId));
            foreach (var message in Events.Where(e => e.ObjectId == RegistryId && e.Opcode == 0).ToArray())
            {
                var name = message.UintAt(0);
                var iface = message.StringAt(4, out _);
                if (iface == "zwp_linux_dmabuf_v1")
                {
                    Send(ManagedWire.Request(RegistryId, 0, name, iface, dmabufVersion, DmabufId));
                }
                else if (iface == "wl_compositor")
                {
                    Send(ManagedWire.Request(RegistryId, 0, name, iface, 4u, CompositorId));
                }
            }
        }

        public ManagedWire.WireMessage? ProtocolError()
        {
            foreach (var message in Events)
            {
                if (message.ObjectId == 1 && message.Opcode == 0)
                {
                    return message;
                }
            }

            return null;
        }

        public void Dispose()
        {
            Display.Dispose();
            Transport.Dispose();
        }
    }

    private static DrmFormatSet LinearXrgb()
    {
        var formats = new DrmFormatSet();
        formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear);
        formats.Add(DrmFormat.Argb8888, DrmFormatSet.ModifierLinear);
        return formats;
    }

    [Fact]
    public void A_token_backed_params_object_creates_a_buffer_over_the_image()
    {
        using var host = new TokenClientHost(LinearXrgb());
        var image = new FakeRemoteImage(8, 4, DrmFormat.Xrgb8888);
        unsafe
        {
            *(uint*)image.Pixels = 0xFF336699;
        }

        var slot = host.Transport.Slots.Mint(image);
        host.BindGlobals();
        host.Send(ManagedWire.Request(DmabufId, 1, ParamsId));
        host.Send(ManagedWire.Request(ParamsId, 1, 0u, 0u, (uint)image.Stride, 0u, 0u), slot);
        host.Send(ManagedWire.Request(ParamsId, 3, BufferId, 8, 4, (uint)DrmFormat.Xrgb8888, 0u));
        host.Send(ManagedWire.Request(CompositorId, 0, SurfaceId));
        host.Send(ManagedWire.Request(SurfaceId, 1, BufferId, 0, 0));
        host.Send(ManagedWire.Request(SurfaceId, 6));

        Assert.Null(host.ProtocolError());
        var surface = Assert.Single(host.Surfaces);
        var buffer = Assert.IsType<RemoteImageBuffer>(surface.Current.Buffer);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        Assert.Equal(image.Pixels, view.Data);
        Assert.Equal(image.Stride, view.Stride);
        unsafe
        {
            Assert.Equal(0xFF336699u, *(uint*)view.Data);
        }

        buffer.EndDataAccess();

        host.Send(ManagedWire.Request(SurfaceId, 1, 0u, 0, 0));
        host.Send(ManagedWire.Request(SurfaceId, 6));
        host.Send(ManagedWire.Request(BufferId, 0));
        Assert.Equal(1, image.References);
        image.Release();
        Assert.True(image.IsReleased);
    }

    [Fact]
    public void A_width_that_disagrees_with_the_image_is_invalid_dimensions()
    {
        using var host = new TokenClientHost(LinearXrgb());
        var image = new FakeRemoteImage(8, 4, DrmFormat.Xrgb8888);
        var slot = host.Transport.Slots.Mint(image);

        host.BindGlobals();
        host.Send(ManagedWire.Request(DmabufId, 1, ParamsId));
        host.Send(ManagedWire.Request(ParamsId, 1, 0u, 0u, (uint)image.Stride, 0u, 0u), slot);
        host.Send(ManagedWire.Request(ParamsId, 3, BufferId, 9, 4, (uint)DrmFormat.Xrgb8888, 0u));

        var error = host.ProtocolError();
        Assert.NotNull(error);
        Assert.Equal(ParamsId, error.Value.UintAt(0));
        Assert.Equal((uint)ZwpLinuxBufferParamsV1.Error.InvalidDimensions, error.Value.UintAt(4));
        Assert.Equal(1, image.References);
        image.Release();
    }

    [Fact]
    public void A_format_that_disagrees_with_the_image_is_invalid_format()
    {
        using var host = new TokenClientHost(LinearXrgb());
        var image = new FakeRemoteImage(8, 4, DrmFormat.Xrgb8888);
        var slot = host.Transport.Slots.Mint(image);

        host.BindGlobals();
        host.Send(ManagedWire.Request(DmabufId, 1, ParamsId));
        host.Send(ManagedWire.Request(ParamsId, 1, 0u, 0u, (uint)image.Stride, 0u, 0u), slot);
        host.Send(ManagedWire.Request(ParamsId, 3, BufferId, 8, 4, (uint)DrmFormat.Argb8888, 0u));

        var error = host.ProtocolError();
        Assert.NotNull(error);
        Assert.Equal((uint)ZwpLinuxBufferParamsV1.Error.InvalidFormat, error.Value.UintAt(4));
        Assert.Equal(1, image.References);
        image.Release();
    }

    [Fact]
    public void A_second_add_naming_another_image_is_invalid_format()
    {
        using var host = new TokenClientHost(LinearXrgb());
        var first = new FakeRemoteImage(8, 4, DrmFormat.Xrgb8888);
        var second = new FakeRemoteImage(8, 4, DrmFormat.Xrgb8888);
        var firstSlot = host.Transport.Slots.Mint(first);
        var secondSlot = host.Transport.Slots.Mint(second);

        host.BindGlobals();
        host.Send(ManagedWire.Request(DmabufId, 1, ParamsId));
        host.Send(ManagedWire.Request(ParamsId, 1, 0u, 0u, (uint)first.Stride, 0u, 0u), firstSlot);
        host.Send(ManagedWire.Request(ParamsId, 1, 1u, 0u, (uint)second.Stride, 0u, 0u), secondSlot);

        var error = host.ProtocolError();
        Assert.NotNull(error);
        Assert.Equal((uint)ZwpLinuxBufferParamsV1.Error.InvalidFormat, error.Value.UintAt(4));
        first.Release();
        second.Release();
    }

    [Fact]
    public void A_token_that_names_no_image_is_invalid_format()
    {
        using var host = new TokenClientHost(LinearXrgb());
        host.BindGlobals();
        host.Send(ManagedWire.Request(DmabufId, 1, ParamsId));
        host.Send(ManagedWire.Request(ParamsId, 1, 0u, 0u, 32u, 0u, 0u), 12345);

        var error = host.ProtocolError();
        Assert.NotNull(error);
        Assert.Equal((uint)ZwpLinuxBufferParamsV1.Error.InvalidFormat, error.Value.UintAt(4));
    }

    [Fact]
    public void A_destroyed_params_object_gives_its_unclaimed_token_back()
    {
        using var host = new TokenClientHost(LinearXrgb());
        var image = new FakeRemoteImage(8, 4, DrmFormat.Xrgb8888);
        var slot = host.Transport.Slots.Mint(image);
        Assert.Equal(2, image.References);

        host.BindGlobals();
        host.Send(ManagedWire.Request(DmabufId, 1, ParamsId));
        host.Send(ManagedWire.Request(ParamsId, 1, 0u, 0u, (uint)image.Stride, 0u, 0u), slot);
        host.Send(ManagedWire.Request(ParamsId, 0));

        Assert.Null(host.ProtocolError());
        Assert.Equal(1, image.References);
        image.Release();
    }
}
