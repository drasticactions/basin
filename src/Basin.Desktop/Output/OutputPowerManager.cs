using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwlr_output_power_manager_v1</c> global. It turns outputs on and off through <see cref="IOutputPower"/>.
/// </summary>
public sealed class OutputPowerManager : IDisposable
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private const uint ErrorInvalidMode = 1;

    private readonly WlGlobal _global;
    private readonly IOutputPower? _power;
    private readonly List<(ZwlrOutputPowerV1Resource Control, IOutput Output)> _controls = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputPowerManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="power">The power implementation. With <see langword="null"/>, each power control fails at once.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public OutputPowerManager(WlServerDisplay display, IOutputPower? power)
    {
        ArgumentNullException.ThrowIfNull(display);
        _power = power;
        _global = display.CreateGlobal(ZwlrOutputPowerManagerV1.Interface, Version, OnBind);
        if (_power is { } live)
        {
            live.PowerChanged += NotifyMode;
        }
    }

    /// <summary>
    /// Stops following power changes and removes the global.
    /// </summary>
    public void Dispose()
    {
        if (_power is { } live)
        {
            live.PowerChanged -= NotifyMode;
        }

        _global.Dispose();
    }

    /// <summary>
    /// Sends the current power mode of an output to every control for it. Without a power implementation, the mode follows <see cref="IOutput.Enabled"/>.
    /// </summary>
    /// <param name="output">The output whose mode changed.</param>
    public void NotifyMode(IOutput output)
    {
        var on = _power?.IsOn(output) ?? output.Enabled;
        foreach (var (control, controlOutput) in _controls)
        {
            if (controlOutput == output && !control.IsDestroyed)
            {
                control.SendMode(on ? ZwlrOutputPowerV1.Mode.On : ZwlrOutputPowerV1.Mode.Off);
            }
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwlrOutputPowerManagerV1Resource(client, version, id);
        manager.GetOutputPower += (_, e) =>
        {
            var control = new ZwlrOutputPowerV1Resource(client, manager.Version, e.Id);
            var output = OutputGlobal.FromResource(e.Output)?.Output;
            if (output is null || _power is null)
            {
                control.SendFailed();
                return;
            }

            var entry = (control, output);
            _controls.Add(entry);
            control.Destroyed += (_, _) => _controls.Remove(entry);

            control.SendMode(_power.IsOn(output) ? ZwlrOutputPowerV1.Mode.On : ZwlrOutputPowerV1.Mode.Off);

            control.SetMode += (_, me) =>
            {
                if (me.Mode is not (ZwlrOutputPowerV1.Mode.On or ZwlrOutputPowerV1.Mode.Off))
                {
                    control.PostError(ErrorInvalidMode, "The power mode is not valid.");
                    return;
                }

                if (!_power.SetOn(output, me.Mode == ZwlrOutputPowerV1.Mode.On))
                {
                    control.SendFailed();
                }
            };
        };
    }
}
