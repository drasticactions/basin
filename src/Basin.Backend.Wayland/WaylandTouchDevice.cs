using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Wayland;

namespace Basin.Backend.Wayland;

/// <summary>
/// Reports the parent seat's touch points on this backend's outputs. A point that goes down outside a known output produces no events at all.
/// </summary>
public sealed class WaylandTouchDevice
{
    private readonly Dictionary<int, WaylandOutput> _outputs = [];
    private readonly WlTouch _touch;

    internal WaylandTouchDevice(WaylandBackend backend, WlTouch touch)
    {
        _touch = touch;
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

    /// <summary>
    /// Occurs when a point goes down on an output. The arguments are the output, the time in milliseconds, the point ID and X and Y in output-local physical pixels.
    /// </summary>
    public event Action<WaylandOutput, uint, int, double, double>? Down;

    /// <summary>Occurs when a point that went down on an output goes up. The arguments are the time in milliseconds and the point ID.</summary>
    public event Action<uint, int>? Up;

    /// <summary>
    /// Occurs when a point moves. The output is the one the point went down on, and X and Y are physical pixels local to it.
    /// </summary>
    public event Action<WaylandOutput, uint, int, double, double>? Motion;

    /// <summary>Occurs when the parent ends a group of touch events that belong together.</summary>
    public event Action? Frame;

    /// <summary>
    /// Occurs when the parent cancels the touch sequence, or when the parent removes the touch device while points are down.
    /// </summary>
    public event Action? Cancel;

    internal void Remove()
    {
        if (_outputs.Count > 0)
        {
            _outputs.Clear();
            Cancel?.Invoke();
        }

        WaylandBackend.ReleaseInputProxy(_touch);
    }
}
