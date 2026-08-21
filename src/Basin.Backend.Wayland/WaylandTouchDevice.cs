using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Wayland;

namespace Basin.Backend.Wayland;

public sealed class WaylandTouchDevice
{
    private readonly Dictionary<int, WaylandOutput> _outputs = [];

    internal WaylandTouchDevice(WaylandBackend backend, WlTouch touch)
    {
        touch.Down += (_, e) =>
        {
            var output = backend.FindOutput(e.Surface);
            if (output is not null)
            {
                _outputs[e.Id] = output;
                Down?.Invoke(output, e.Time, e.Id, e.X.ToDouble() * output.SurfaceToPhysical, e.Y.ToDouble() * output.SurfaceToPhysical);
            }
        };
        touch.Up += (_, e) =>
        {
            if (_outputs.Remove(e.Id))
            {
                Up?.Invoke(e.Time, e.Id);
            }
        };
        touch.Motion += (_, e) =>
        {
            if (_outputs.TryGetValue(e.Id, out var output))
            {
                Motion?.Invoke(output, e.Time, e.Id, e.X.ToDouble() * output.SurfaceToPhysical, e.Y.ToDouble() * output.SurfaceToPhysical);
            }
        };
        touch.Frame += (_, _) => Frame?.Invoke();
        touch.Cancel += (_, _) =>
        {
            _outputs.Clear();
            Cancel?.Invoke();
        };
    }

    public event Action<WaylandOutput, uint, int, double, double>? Down;

    public event Action<uint, int>? Up;

    public event Action<WaylandOutput, uint, int, double, double>? Motion;

    public event Action? Frame;

    public event Action? Cancel;
}
