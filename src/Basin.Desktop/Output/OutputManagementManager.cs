using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwlr_output_manager_v1</c> global. It reports heads and modes, and applies client configurations through <see cref="IOutputConfiguration"/>.
/// </summary>
public sealed class OutputManagementManager : IDisposable
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputManagementManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global and supplies serials.</param>
    /// <param name="layout">The layout whose changes cause a refresh.</param>
    /// <param name="outputs">The set of outputs to report as heads.</param>
    /// <param name="configuration">The configuration that applies client requests, or <see langword="null"/> to fail every apply.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/>, <paramref name="layout"/> or <paramref name="outputs"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Stops following the layout and the output set, and removes the global.
    /// </summary>
    public void Dispose()
    {
        _layout.Changed -= Refresh;
        _outputs.Changed -= Sync;
        _global.Dispose();
    }

    /// <summary>
    /// Sends the state of every head to every bound manager, followed by <c>done</c> with a new serial.
    /// </summary>
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
        var binding = new ManagerBinding(this, client, version, id);
        var manager = binding.Resource;
        var heads = binding.Heads;
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

    private sealed class ManagerBinding : IZwlrOutputManagerV1Handler
    {
        private readonly OutputManagementManager _owner;

        public ManagerBinding(OutputManagementManager owner, WlClient client, uint version, uint id)
        {
            _owner = owner;
            Resource = new ZwlrOutputManagerV1Resource(client, version, id, this);
        }

        public ZwlrOutputManagerV1Resource Resource { get; }

        public List<HeadState> Heads { get; } = [];

        void IZwlrOutputManagerV1Handler.CreateConfiguration(
            ZwlrOutputManagerV1Resource resource,
            in ZwlrOutputManagerV1Resource.CreateConfigurationEventArgs args) =>
            _ = new Configuration(_owner, resource.Client, resource.Version, args.Id, args.Serial, Heads);

        void IZwlrOutputManagerV1Handler.Stop(ZwlrOutputManagerV1Resource resource)
        {
            resource.SendFinished();
            resource.Destroy();
        }
    }

    private sealed class Configuration : IZwlrOutputConfigurationV1Handler
    {
        private readonly OutputManagementManager _owner;
        private readonly ZwlrOutputConfigurationV1Resource _resource;
        private readonly List<HeadState> _heads;
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

        public Configuration(OutputManagementManager owner, WlClient client, uint version, uint id, uint serial, List<HeadState> heads)
        {
            _owner = owner;
            _heads = heads;
            _stale = serial != owner._serial;
            _resource = new ZwlrOutputConfigurationV1Resource(client, version, id, this);
        }

        void IZwlrOutputConfigurationV1Handler.EnableHead(
            ZwlrOutputConfigurationV1Resource resource,
            in ZwlrOutputConfigurationV1Resource.EnableHeadEventArgs args)
        {
            var target = args.Head;
            var head = _heads.FirstOrDefault(h => h.Head == target);
            if (head is null)
            {
                return;
            }

            var entryIndex = _changes.Count;
            _ = new ConfigurationHead(this, head, entryIndex, resource.Client, resource.Version, args.Id);
            _changes.Add(new Change(head, true, null, null, null, null, null));
            _configured.Add(head.Output);
        }

        void IZwlrOutputConfigurationV1Handler.DisableHead(
            ZwlrOutputConfigurationV1Resource resource,
            in ZwlrOutputConfigurationV1Resource.DisableHeadEventArgs args)
        {
            var target = args.Head;
            var head = _heads.FirstOrDefault(h => h.Head == target);
            if (head is not null)
            {
                _changes.Add(new Change(head, false, null, null, null, null, null));
                _configured.Add(head.Output);
            }
        }

        void IZwlrOutputConfigurationV1Handler.Test(ZwlrOutputConfigurationV1Resource resource) => Finish(apply: false);

        void IZwlrOutputConfigurationV1Handler.Apply(ZwlrOutputConfigurationV1Resource resource) => Finish(apply: true);

        void IZwlrOutputConfigurationV1Handler.Destroy(ZwlrOutputConfigurationV1Resource resource)
        {
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

        private sealed class ConfigurationHead : IZwlrOutputConfigurationHeadV1Handler
        {
            private readonly Configuration _configuration;
            private readonly HeadState _head;
            private readonly int _entryIndex;

            public ConfigurationHead(Configuration configuration, HeadState head, int entryIndex, WlClient client, uint version, uint id)
            {
                _configuration = configuration;
                _head = head;
                _entryIndex = entryIndex;
                _ = new ZwlrOutputConfigurationHeadV1Resource(client, version, id, this);
            }

            private List<Change> Changes => _configuration._changes;

            void IZwlrOutputConfigurationHeadV1Handler.SetMode(
                ZwlrOutputConfigurationHeadV1Resource resource,
                in ZwlrOutputConfigurationHeadV1Resource.SetModeEventArgs args)
            {
                var target = args.Mode;
                var mode = _head.Modes.FirstOrDefault(m => m.Resource == target);
                Changes[_entryIndex] = Changes[_entryIndex] with { Mode = mode.Mode };
            }

            void IZwlrOutputConfigurationHeadV1Handler.SetCustomMode(
                ZwlrOutputConfigurationHeadV1Resource resource,
                in ZwlrOutputConfigurationHeadV1Resource.SetCustomModeEventArgs args) =>
                Changes[_entryIndex] = Changes[_entryIndex] with { Mode = new OutputMode(args.Width, args.Height, args.Refresh) };

            void IZwlrOutputConfigurationHeadV1Handler.SetPosition(
                ZwlrOutputConfigurationHeadV1Resource resource,
                in ZwlrOutputConfigurationHeadV1Resource.SetPositionEventArgs args) =>
                Changes[_entryIndex] = Changes[_entryIndex] with { Position = new Point(args.X, args.Y) };

            void IZwlrOutputConfigurationHeadV1Handler.SetScale(
                ZwlrOutputConfigurationHeadV1Resource resource,
                in ZwlrOutputConfigurationHeadV1Resource.SetScaleEventArgs args) =>
                Changes[_entryIndex] = Changes[_entryIndex] with { Scale = args.Scale.ToDouble() };

            void IZwlrOutputConfigurationHeadV1Handler.SetTransform(
                ZwlrOutputConfigurationHeadV1Resource resource,
                in ZwlrOutputConfigurationHeadV1Resource.SetTransformEventArgs args) =>
                Changes[_entryIndex] = Changes[_entryIndex] with { Transform = (OutputTransform)args.Transform };

            void IZwlrOutputConfigurationHeadV1Handler.SetAdaptiveSync(
                ZwlrOutputConfigurationHeadV1Resource resource,
                in ZwlrOutputConfigurationHeadV1Resource.SetAdaptiveSyncEventArgs args)
            {
                if (args.State is not (ZwlrOutputHeadV1.AdaptiveSyncState.Disabled or ZwlrOutputHeadV1.AdaptiveSyncState.Enabled))
                {
                    resource.PostError(
                        (uint)ZwlrOutputConfigurationHeadV1.Error.InvalidAdaptiveSyncState,
                        $"Adaptive sync state {(uint)args.State} is not a known value.");
                    return;
                }

                Changes[_entryIndex] = Changes[_entryIndex] with
                {
                    AdaptiveSync = args.State == ZwlrOutputHeadV1.AdaptiveSyncState.Enabled,
                };
            }
        }
    }
}
