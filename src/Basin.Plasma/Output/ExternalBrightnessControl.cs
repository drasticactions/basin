using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class ExternalBrightnessControl : IOutputBrightness
{
    private readonly ExternalBrightnessManager _owner;

    internal ExternalBrightnessControl(ExternalBrightnessManager owner) => _owner = owner;

    public event Action<IOutput>? BrightnessChanged;

    public bool Supports(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _owner.DeviceFor(output) is not null;
    }

    public uint Max(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _owner.DeviceFor(output)?.MaxBrightness ?? 0;
    }

    public bool TryGet(IOutput output, out uint value)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (_owner.DeviceFor(output) is { } device &&
            (device.ObservedBrightness ?? device.RequestedBrightness) is { } known)
        {
            value = known;
            return true;
        }

        value = 0;
        return false;
    }

    public bool UsesDdcCi(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _owner.DeviceFor(output)?.UsesDdcCi == true;
    }

    public bool Set(IOutput output, uint value)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (_owner.DeviceFor(output) is not { } device)
        {
            return false;
        }

        device.Request(Math.Min(value, device.MaxBrightness));
        return true;
    }

    internal void NotifyChanged(IOutput output) => BrightnessChanged?.Invoke(output);
}
