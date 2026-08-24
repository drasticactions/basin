using Basin.Capabilities;
using Basin.Plasma.Protocol;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class ExternalBrightnessManager : IDisposable
{
    public const int Version = 3;

    private readonly ICompositorEventLoop _loop;
    private readonly IOutputSet? _outputs;
    private readonly WlGlobal _global;
    private readonly List<ExternalBrightnessDevice> _devices = [];

    public ExternalBrightnessManager(WlServerDisplay display, ICompositorEventLoop loop, IOutputSet? outputs)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
        _outputs = outputs;
        Control = new ExternalBrightnessControl(this);
        _global = display.CreateGlobal(KdeExternalBrightnessV1.Interface, Version, OnBind);
        if (outputs is not null)
        {
            outputs.Changed += RematchAll;
        }
    }

    public ExternalBrightnessControl Control { get; }

    public IReadOnlyList<ExternalBrightnessDevice> Devices => _devices;

    public void Dispose()
    {
        if (_outputs is not null)
        {
            _outputs.Changed -= RematchAll;
        }

        _global.Dispose();
    }

    internal void Unregister(ExternalBrightnessDevice device)
    {
        var output = device.Output;
        _devices.Remove(device);
        if (output is not null)
        {
            Control.NotifyChanged(output);
        }
    }

    internal void OnDeviceCommitted(ExternalBrightnessDevice device, bool first, bool observedChanged)
    {
        var previous = device.Output;
        Match(device);
        if (device.Output != previous && previous is not null)
        {
            Control.NotifyChanged(previous);
        }

        if (device.Output is { } output && (first || observedChanged || device.Output != previous))
        {
            Control.NotifyChanged(output);
        }
    }

    internal ExternalBrightnessDevice? DeviceFor(IOutput output)
    {
        foreach (var device in _devices)
        {
            if (device.Output == output)
            {
                return device;
            }
        }

        return null;
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new KdeExternalBrightnessV1Resource(client, version, id);
        resource.CreateBrightnessControl += (_, e) =>
        {
            var device = new ExternalBrightnessDevice(
                this, new KdeExternalBrightnessDeviceV1Resource(client, resource.Version, e.Id), _loop);
            _devices.Add(device);
        };
    }

    private void RematchAll()
    {
        foreach (var device in _devices)
        {
            var previous = device.Output;
            Match(device);
            if (device.Output != previous)
            {
                if (previous is not null)
                {
                    Control.NotifyChanged(previous);
                }

                if (device.Output is { } output)
                {
                    Control.NotifyChanged(output);
                }
            }
        }
    }

    private void Match(ExternalBrightnessDevice device)
    {
        device.Output = null;
        var outputs = _outputs?.Outputs ?? [];
        if (device.EdidBlock.Length >= 128)
        {
            IOutput? matched = null;
            var matches = 0;
            foreach (var output in outputs)
            {
                var edid = output.EdidBytes;
                if (edid.Length >= 128 && edid.Span[..128].SequenceEqual(device.EdidBlock.Span[..128]))
                {
                    matched = output;
                    matches++;
                }
            }

            if (matches == 1)
            {
                device.Output = matched;
                return;
            }

            if (matches > 1)
            {
                return;
            }
        }

        if (device.IsInternal)
        {
            foreach (var output in outputs)
            {
                if (InternalConnectors.IsInternal(output))
                {
                    device.Output = output;
                    return;
                }
            }
        }
    }
}
