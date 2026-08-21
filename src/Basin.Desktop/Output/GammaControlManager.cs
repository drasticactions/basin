using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class GammaControlManager : IDisposable
{
    public const int Version = 1;

    private const uint ErrorInvalidGamma = 1;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    private readonly WlGlobal _global;
    private readonly IOutputGamma? _gamma;
    private readonly Dictionary<IOutput, ZwlrGammaControlV1Resource> _controls = [];

    public GammaControlManager(WlServerDisplay display, IOutputGamma? gamma)
    {
        ArgumentNullException.ThrowIfNull(display);
        _gamma = gamma;
        _global = display.CreateGlobal(ZwlrGammaControlManagerV1.Interface, Version, OnBind);
    }

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwlrGammaControlManagerV1Resource(client, version, id);
        manager.GetGammaControl += (_, e) =>
        {
            var control = new ZwlrGammaControlV1Resource(client, manager.Version, e.Id);
            var output = OutputGlobal.FromResource(e.Output)?.Output;
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

            if (_controls.TryGetValue(output, out var previous) && !previous.IsDestroyed)
            {
                previous.SendFailed();
            }

            _controls[output] = control;
            control.Destroyed += (_, _) =>
            {
                if (_controls.TryGetValue(output, out var current) && current == control)
                {
                    _controls.Remove(output);
                    _gamma!.Reset(output);
                }
            };

            control.SendGammaSize(size);
            control.SetGamma += (_, ge) => OnSetGamma(control, output, size, ge.Fd);
        };
    }

    private unsafe void OnSetGamma(ZwlrGammaControlV1Resource control, IOutput output, uint size, int fd)
    {
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
            control.PostError(ErrorInvalidGamma, "gamma table too short");
            return;
        }

        var ramps = new OutputGammaRamps(new ushort[size], new ushort[size], new ushort[size]);
        System.Buffer.BlockCopy(bytes, 0, ramps.Red, 0, (int)size * 2);
        System.Buffer.BlockCopy(bytes, (int)size * 2, ramps.Green, 0, (int)size * 2);
        System.Buffer.BlockCopy(bytes, (int)size * 4, ramps.Blue, 0, (int)size * 2);

        if (_gamma is null || !_gamma.Apply(output, ramps))
        {
            control.SendFailed();
        }
    }
}
