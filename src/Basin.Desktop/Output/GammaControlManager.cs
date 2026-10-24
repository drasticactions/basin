using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwlr_gamma_control_manager_v1</c> global. It reads gamma ramps that clients send and applies them through <see cref="IOutputGamma"/>.
/// </summary>
public sealed class GammaControlManager : IDisposable, IZwlrGammaControlManagerV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private const uint ErrorInvalidGamma = 1;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    private readonly WlGlobal _global;
    private readonly IOutputGamma? _gamma;
    private readonly Dictionary<IOutput, GammaControl> _controls = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="GammaControlManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="gamma">The gamma implementation. With <see langword="null"/>, each gamma control fails at once.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public GammaControlManager(WlServerDisplay display, IOutputGamma? gamma)
    {
        ArgumentNullException.ThrowIfNull(display);
        _gamma = gamma;
        _global = display.CreateGlobal(ZwlrGammaControlManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Removes the global. Bound clients keep their resources.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new ZwlrGammaControlManagerV1Resource(client, version, id, this);

    void IZwlrGammaControlManagerV1Handler.Destroy(ZwlrGammaControlManagerV1Resource resource)
    {
    }

    void IZwlrGammaControlManagerV1Handler.GetGammaControl(
        ZwlrGammaControlManagerV1Resource resource,
        in ZwlrGammaControlManagerV1Resource.GetGammaControlEventArgs args)
    {
        var handler = new GammaControl(this, resource.Client, resource.Version, args.Id);
        var control = handler.Resource;
        var output = OutputGlobal.FromResource(args.Output)?.Output;
        if (output is null)
        {
            control.SendFailed();
            return;
        }

        var size = _gamma?.RampSize(output) ?? 0;
        if (size == 0)
        {
            control.SendFailed();
            return;
        }

        if (_controls.TryGetValue(output, out var previous) && !previous.Resource.IsDestroyed)
        {
            previous.Fail();
        }

        _controls[output] = handler;
        handler.Attach(output, size);
    }

    private unsafe void OnSetGamma(GammaControl handler, IOutput output, uint size, int fd)
    {
        var control = handler.Resource;
        var bytes = new byte[size * 3 * sizeof(ushort)];
        var total = 0;
        fixed (byte* buffer = bytes)
        {
            while (total < bytes.Length)
            {
                var got = read(fd, buffer + total, (nuint)(bytes.Length - total));
                if (got <= 0)
                {
                    break;
                }

                total += (int)got;
            }
        }

        control.Client.CloseFd(fd);
        if (total != bytes.Length)
        {
            control.PostError(ErrorInvalidGamma, "The gamma table is too short.");
            return;
        }

        var ramps = new OutputGammaRamps(new ushort[size], new ushort[size], new ushort[size]);
        System.Buffer.BlockCopy(bytes, 0, ramps.Red, 0, (int)size * 2);
        System.Buffer.BlockCopy(bytes, (int)size * 2, ramps.Green, 0, (int)size * 2);
        System.Buffer.BlockCopy(bytes, (int)size * 4, ramps.Blue, 0, (int)size * 2);

        if (_gamma is null || !_gamma.Apply(output, ramps))
        {
            handler.Fail();
        }
    }

    private sealed class GammaControl : IZwlrGammaControlV1Handler
    {
        private readonly GammaControlManager _manager;
        private IOutput? _output;
        private uint _size;
        private bool _failed;

        public GammaControl(GammaControlManager manager, WlClient client, uint version, uint id)
        {
            _manager = manager;
            Resource = new ZwlrGammaControlV1Resource(client, version, id, this);
        }

        public ZwlrGammaControlV1Resource Resource { get; }

        public void Attach(IOutput output, uint size)
        {
            _output = output;
            _size = size;
            Resource.Destroyed += OnDestroyed;
            Resource.SendGammaSize(size);
        }

        public void Fail()
        {
            _failed = true;
            Resource.SendFailed();
        }

        void IZwlrGammaControlV1Handler.SetGamma(ZwlrGammaControlV1Resource resource, in ZwlrGammaControlV1Resource.SetGammaEventArgs args)
        {
            if (_output is not { } output || _failed)
            {
                resource.Client.CloseFd(args.Fd);
                return;
            }

            _manager.OnSetGamma(this, output, _size, args.Fd);
        }

        void IZwlrGammaControlV1Handler.Destroy(ZwlrGammaControlV1Resource resource)
        {
        }

        private void OnDestroyed(object? sender, EventArgs e)
        {
            var output = _output!;
            if (_manager._controls.TryGetValue(output, out var current) && current == this)
            {
                _manager._controls.Remove(output);
                _manager._gamma!.Reset(output);
            }
        }
    }
}
