using Basin.Capabilities;
using Basin.Plasma.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class DpmsManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly IOutputPower? _power;
    private readonly List<(OrgKdeKwinDpmsResource Resource, IOutput Output)> _resources = [];

    public DpmsManager(WlServerDisplay display, IOutputPower? power)
    {
        ArgumentNullException.ThrowIfNull(display);
        _power = power;
        _global = display.CreateGlobal(OrgKdeKwinDpmsManager.Interface, Version, OnBind);
        if (_power is { } live)
        {
            live.PowerChanged += NotifyMode;
        }
    }

    public void Dispose()
    {
        if (_power is { } live)
        {
            live.PowerChanged -= NotifyMode;
        }

        _global.Dispose();
    }

    public void NotifyMode(IOutput output)
    {
        var mode = CurrentMode(output);
        foreach (var (resource, resourceOutput) in _resources)
        {
            if (resourceOutput == output && !resource.IsDestroyed)
            {
                resource.SendMode((uint)mode);
                resource.SendDone();
            }
        }
    }

    private OrgKdeKwinDpms.Mode CurrentMode(IOutput output) =>
        _power is { } live && !live.IsOn(output) ? OrgKdeKwinDpms.Mode.Off : OrgKdeKwinDpms.Mode.On;

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdeKwinDpmsManagerResource(client, version, id);
        manager.Get += (_, e) =>
        {
            var resource = new OrgKdeKwinDpmsResource(client, manager.Version, e.Id);
            var output = OutputGlobal.FromResource(e.Output)?.Output;
            var supported = _power is not null && output is not null;
            if (output is not null)
            {
                var entry = (resource, output);
                _resources.Add(entry);
                Action onOutputDestroyed = () => _resources.Remove(entry);
                output.Destroyed += onOutputDestroyed;
                resource.Destroyed += (_, _) =>
                {
                    _resources.Remove(entry);
                    output.Destroyed -= onOutputDestroyed;
                };
            }

            resource.SendSupported(supported ? 1u : 0u);
            resource.SendMode(output is null ? (uint)OrgKdeKwinDpms.Mode.On : (uint)CurrentMode(output));
            resource.SendDone();

            resource.Set += (_, se) =>
            {
                if (!supported || se.Mode > (uint)OrgKdeKwinDpms.Mode.Off)
                {
                    return;
                }

                _power!.SetOn(output!, se.Mode == (uint)OrgKdeKwinDpms.Mode.On);
            };
        };
    }
}
