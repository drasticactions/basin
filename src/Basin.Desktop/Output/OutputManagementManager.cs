using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class OutputManagementManager : IDisposable
{
    public const int Version = 4;

    private readonly WlServerDisplay _display;
    private readonly WlGlobal _global;
    private readonly OutputLayout _layout;
    private readonly IOutputSet _outputs;
    private readonly IOutputConfiguration? _configuration;
    private readonly List<(ZwlrOutputManagerV1Resource Manager, List<HeadState> Heads)> _managers = [];
    private uint _serial;

    private sealed class HeadState
    {
        public required IOutput Output;
        public required ZwlrOutputHeadV1Resource Head;
        public required List<(ZwlrOutputModeV1Resource Resource, OutputMode Mode)> Modes;
    }

    public OutputManagementManager(
        WlServerDisplay display, OutputLayout layout, IOutputSet outputs, IOutputConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(outputs);
        _display = display;
        _layout = layout;
        _outputs = outputs;
        _configuration = configuration;
        _global = display.CreateGlobal(ZwlrOutputManagerV1.Interface, Version, OnBind);
        layout.Changed += Refresh;
        outputs.Changed += Sync;
    }

    public void Dispose()
    {
        _layout.Changed -= Refresh;
        _outputs.Changed -= Sync;
        _global.Dispose();
    }

    public void Refresh()
    {
        _serial = _display.NextSerial();
        foreach (var (manager, heads) in _managers)
        {
            if (manager.IsDestroyed)
            {
                continue;
            }

            foreach (var head in heads)
            {
                SendHeadState(head);
            }

            manager.SendDone(_serial);
        }
    }

    private void Sync()
    {
        foreach (var (manager, heads) in _managers)
        {
            if (manager.IsDestroyed)
            {
                continue;
            }

            for (var i = heads.Count - 1; i >= 0; i--)
            {
                if (!_outputs.Outputs.Contains(heads[i].Output))
                {
                    RetireHead(heads[i]);
                    heads.RemoveAt(i);
                }
            }

            foreach (var output in _outputs.Outputs)
            {
                if (!heads.Exists(head => head.Output == output))
                {
                    heads.Add(CreateHead(manager, output));
                }
            }
        }

        Refresh();
    }

    private static void RetireHead(HeadState head)
    {
        foreach (var (resource, _) in head.Modes)
        {
            resource.SendFinished();
        }

        head.Head.SendFinished();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwlrOutputManagerV1Resource(client, version, id);
        var heads = new List<HeadState>();
        _managers.Add((manager, heads));
        manager.Destroyed += (_, _) => _managers.RemoveAll(entry => entry.Manager == manager);

        foreach (var output in _outputs.Outputs)
        {
            heads.Add(CreateHead(manager, output));
        }

        if (_serial == 0)
        {
            _serial = _display.NextSerial();
        }

        manager.SendDone(_serial);

        manager.CreateConfiguration += (_, e) =>
        {
            var configuration = new ZwlrOutputConfigurationV1Resource(client, manager.Version, e.Id);
            _ = new Configuration(this, configuration, e.Serial, heads);
        };
    }

    private HeadState CreateHead(ZwlrOutputManagerV1Resource manager, IOutput output)
    {
        var head = new ZwlrOutputHeadV1Resource(manager.Client, manager.Version, 0);
        manager.SendHead(head);
        head.SendName(output.Name);
        head.SendDescription(output.Description);
        var (physicalWidth, physicalHeight) = output.PhysicalSize;
        if (physicalWidth > 0)
        {
            head.SendPhysicalSize(physicalWidth, physicalHeight);
        }

        if (head.Version >= 2)
        {
            if (output.Make.Length > 0)
            {
                head.SendMake(output.Make);
            }

            if (output.Model.Length > 0)
            {
                head.SendModel(output.Model);
            }

            if (output.Serial.Length > 0)
            {
                head.SendSerialNumber(output.Serial);
            }
        }

        var modes = new List<(ZwlrOutputModeV1Resource, OutputMode)>();
        var allModes = (output as Basin.Backend.Drm.DrmOutput)?.Modes ?? [output.CurrentMode];
        foreach (var mode in allModes)
        {
            var modeResource = new ZwlrOutputModeV1Resource(manager.Client, manager.Version, 0);
            head.SendMode(modeResource);
            modeResource.SendSize(mode.Width, mode.Height);
            modeResource.SendRefresh(mode.RefreshMilliHz);
            modes.Add((modeResource, mode));
        }

        var state = new HeadState { Output = output, Head = head, Modes = modes };
        SendHeadState(state);
        return state;
    }

    private void SendHeadState(HeadState state)
    {
        var output = state.Output;
        var exposed = _layout.Contains(output);
        state.Head.SendEnabled(exposed ? 1 : 0);
        if (exposed)
        {
            var current = state.Modes.FirstOrDefault(m => m.Mode == output.CurrentMode);
            if (current.Resource is not null)
            {
                state.Head.SendCurrentMode(current.Resource);
            }

            var box = _layout.BoxOf(output);
            state.Head.SendPosition(box.X, box.Y);
            state.Head.SendTransform((WlOutput.Transform)output.Transform);
            state.Head.SendScale(WlFixed.FromDouble(output.Scale));
            if (state.Head.Version >= 4)
            {
                state.Head.SendAdaptiveSync(output.AdaptiveSync
                    ? ZwlrOutputHeadV1.AdaptiveSyncState.Enabled
                    : ZwlrOutputHeadV1.AdaptiveSyncState.Disabled);
            }
        }
    }

    private sealed class Configuration
    {
        private readonly OutputManagementManager _owner;
        private readonly ZwlrOutputConfigurationV1Resource _resource;
        private readonly bool _stale;
        private readonly List<Change> _changes = [];

        private readonly record struct Change(
            HeadState Head,
            bool Enable,
            OutputMode? Mode,
            Point? Position,
            double? Scale,
            OutputTransform? Transform,
            bool? AdaptiveSync);
        private readonly HashSet<IOutput> _configured = [];

        public Configuration(OutputManagementManager owner, ZwlrOutputConfigurationV1Resource resource, uint serial, List<HeadState> heads)
        {
            _owner = owner;
            _resource = resource;
            _stale = serial != owner._serial;

            resource.EnableHead += (_, e) =>
            {
                var head = heads.FirstOrDefault(h => h.Head == e.Head);
                if (head is null)
                {
                    return;
                }

                var configurationHead = new ZwlrOutputConfigurationHeadV1Resource(resource.Client, resource.Version, e.Id);
                var entryIndex = _changes.Count;
                _changes.Add(new Change(head, true, null, null, null, null, null));
                _configured.Add(head.Output);
                configurationHead.SetMode += (_, me) =>
                {
                    var mode = head.Modes.FirstOrDefault(m => m.Resource == me.Mode);
                    _changes[entryIndex] = _changes[entryIndex] with { Mode = mode.Mode };
                };
                configurationHead.SetCustomMode += (_, me) =>
                    _changes[entryIndex] = _changes[entryIndex] with { Mode = new OutputMode(me.Width, me.Height, me.Refresh) };
                configurationHead.SetPosition += (_, pe) =>
                    _changes[entryIndex] = _changes[entryIndex] with { Position = new Point(pe.X, pe.Y) };
                configurationHead.SetScale += (_, se) =>
                    _changes[entryIndex] = _changes[entryIndex] with { Scale = se.Scale.ToDouble() };
                configurationHead.SetTransform += (_, te) =>
                    _changes[entryIndex] = _changes[entryIndex] with { Transform = (OutputTransform)te.Transform };
                configurationHead.SetAdaptiveSync += (_, ae) =>
                {
                    if (ae.State is not (ZwlrOutputHeadV1.AdaptiveSyncState.Disabled or ZwlrOutputHeadV1.AdaptiveSyncState.Enabled))
                    {
                        configurationHead.PostError(
                            (uint)ZwlrOutputConfigurationHeadV1.Error.InvalidAdaptiveSyncState,
                            $"invalid adaptive sync state {(uint)ae.State}");
                        return;
                    }

                    _changes[entryIndex] = _changes[entryIndex] with
                    {
                        AdaptiveSync = ae.State == ZwlrOutputHeadV1.AdaptiveSyncState.Enabled,
                    };
                };
            };
            resource.DisableHead += (_, e) =>
            {
                var head = heads.FirstOrDefault(h => h.Head == e.Head);
                if (head is not null)
                {
                    _changes.Add(new Change(head, false, null, null, null, null, null));
                    _configured.Add(head.Output);
                }
            };
            resource.Test += (_, _) => Finish(apply: false);
            resource.Apply += (_, _) => Finish(apply: true);
        }

        private void Finish(bool apply)
        {
            if (_stale || _owner._configuration is not { } configuration)
            {
                _resource.SendCancelled();
                return;
            }

            var entries = new List<OutputConfigurationEntry>(_changes.Count);
            foreach (var change in _changes)
            {
                if (change.Scale is <= 0)
                {
                    _resource.SendFailed();
                    return;
                }

                entries.Add(new OutputConfigurationEntry
                {
                    Output = change.Head.Output,
                    Enabled = change.Enable,
                    Mode = change.Mode,
                    Position = change.Position,
                    Scale = change.Scale,
                    Transform = change.Transform,
                    AdaptiveSync = change.AdaptiveSync,
                });
            }

            var ok = apply ? configuration.Apply(entries) : configuration.Test(entries);
            if (!ok)
            {
                _resource.SendFailed();
                return;
            }

            _resource.SendSucceeded();
            if (apply)
            {
                _owner.Refresh();
            }
        }
    }
}
