using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// Advertises one <see cref="IOutput"/> as a <c>wl_output</c> global and sends its geometry, mode and scale again after each commit that changes them. An output without a global still renders, but no client learns of it.
/// </summary>
public sealed class OutputGlobal : IDisposable
{
    /// <summary>
    /// The highest <c>wl_output</c> version that the global advertises.
    /// </summary>
    public const int Version = 4;

    private readonly WlServerDisplay _display;
    private readonly WlGlobal _global;
    private readonly IOutput _output;
    private readonly List<WlOutputResource> _resources = [];
    private readonly Dictionary<WlClient, ClientView> _overrides = [];
    private bool _disposed;

    private readonly record struct ClientView(OutputMode Mode, int Scale);

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputGlobal"/> class and creates the global. <see cref="For"/> finds it from the output until <see cref="Dispose"/>.
    /// </summary>
    /// <param name="display">The display that owns the global.</param>
    /// <param name="output">The output to advertise.</param>
    public OutputGlobal(WlServerDisplay display, IOutput output)
    {
        ByOutput[output] = this;
        _display = display;
        _output = output;
        _advertised = Snapshot();
        _global = display.CreateGlobal(WlOutput.Interface, Version, OnBind);
        _output.Committed += OnOutputCommitted;
        _output.ModesChanged += OnModesChanged;
    }

    /// <summary>
    /// Gets the output that the global advertises.
    /// </summary>
    public IOutput Output => _output;

    /// <summary>
    /// Gets the registry name of the global as one client sees it.
    /// </summary>
    /// <param name="client">The client that asks.</param>
    /// <returns>The name in the client's registry.</returns>
    public uint NameFor(WlClient client) => _global.NameFor(client);

    /// <summary>
    /// Sends <c>done</c> on each of the client's resources of version 2 or higher. Another protocol calls it after it sends its own output events.
    /// </summary>
    /// <param name="client">The client to send to.</param>
    public void SendDone(WlClient client)
    {
        foreach (var resource in ResourcesOf(client))
        {
            if (resource.Version >= 2)
            {
                resource.SendDone();
            }
        }
    }

    /// <summary>
    /// Gets every live resource that clients bound to the global.
    /// </summary>
    public IReadOnlyList<WlOutputResource> Resources => _resources;

    /// <summary>
    /// Lists the resources that one client bound to the global.
    /// </summary>
    /// <param name="client">The client whose resources to list.</param>
    /// <returns>The client's resources, which can be empty.</returns>
    public IEnumerable<WlOutputResource> ResourcesOf(WlClient client)
    {
        foreach (var resource in _resources)
        {
            if (resource.Client == client)
            {
                yield return resource;
            }
        }
    }

    /// <summary>
    /// Shows one client another mode and scale than the output has, for example an X server that works in physical pixels. The client gets the new state at once, and the override ends when the client is destroyed.
    /// </summary>
    /// <param name="client">The client that sees the override.</param>
    /// <param name="mode">The mode to send to that client.</param>
    /// <param name="scale">The integer scale to send. A value below 1 becomes 1.</param>
    public void SetClientOverride(WlClient client, OutputMode mode, int scale)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (_disposed)
        {
            return;
        }

        var view = new ClientView(mode, Math.Max(1, scale));
        if (_overrides.TryGetValue(client, out var existing))
        {
            if (existing == view)
            {
                return;
            }
        }
        else
        {
            client.Destroyed += () => _overrides.Remove(client);
        }

        _overrides[client] = view;
        ResendTo(client);
    }

    /// <summary>
    /// Removes the override of one client and sends it the real state again. It does nothing when the client has no override.
    /// </summary>
    /// <param name="client">The client whose override ends.</param>
    public void ClearClientOverride(WlClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!_overrides.Remove(client) || _disposed)
        {
            return;
        }

        ResendTo(client);
    }

    private void ResendTo(WlClient client)
    {
        foreach (var resource in _resources)
        {
            if (resource.Client == client)
            {
                SendState(resource, sendName: false, sendDescription: false);
            }
        }
    }

    /// <summary>
    /// Destroys the global at once and stops the tracking of the output. Prefer <see cref="Retire"/> for an output that goes away while clients run.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _overrides.Clear();
        _output.Committed -= OnOutputCommitted;
        _output.ModesChanged -= OnModesChanged;
        _ = ByOutput.TryRemove(new KeyValuePair<IOutput, OutputGlobal>(_output, this));

        _global.Dispose();
    }

    /// <summary>
    /// Removes the global from the registry now and disposes it later, so a client that binds it during the removal does not fail.
    /// </summary>
    /// <param name="graceMillis">The maximum delay in milliseconds before the dispose.</param>
    public void Retire(int graceMillis = GlobalRetirement.DefaultGraceMillis) =>
        GlobalRetirement.Retire(_display, _global, Dispose, graceMillis);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<WlOutputResource, OutputGlobal> ByResource = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IOutput, OutputGlobal> ByOutput = new();

    /// <summary>
    /// Finds the global that advertises an output.
    /// </summary>
    /// <param name="output">The output to look up.</param>
    /// <returns>The global, or <see langword="null"/> when the output has none.</returns>
    public static OutputGlobal? For(IOutput output) =>
        output is null ? null : ByOutput.GetValueOrDefault(output);

    /// <summary>
    /// Finds the global that a <c>wl_output</c> resource was bound to.
    /// </summary>
    /// <param name="resource">The resource that a client sent.</param>
    /// <returns>The global, or <see langword="null"/> when the resource is <see langword="null"/> or not known.</returns>
    public static OutputGlobal? FromResource(WlOutputResource? resource) =>
        resource is not null && ByResource.TryGetValue(resource, out var global) ? global : null;

    /// <summary>
    /// Occurs after a client binds the global and receives the initial state.
    /// </summary>
    public event Action<WlOutputResource>? ResourceBound;

    private Point _position;

    internal void NotifyPosition(int x, int y)
    {
        if (_position.X == x && _position.Y == y)
        {
            return;
        }

        _position = new Point(x, y);
        foreach (var resource in _resources)
        {
            SendState(resource, sendName: false, sendDescription: false);
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new WlOutputResource(client, version, id);
        _resources.Add(resource);
        ByResource[resource] = this;
        resource.Destroyed += (_, _) =>
        {
            _resources.Remove(resource);
            ByResource.TryRemove(resource, out _);
        };
        SendState(resource, sendName: true, sendDescription: true);
        ResourceBound?.Invoke(resource);
    }

    private (int PhysicalWidth, int PhysicalHeight, OutputMode Mode, int Scale, OutputTransform Transform, string Description) _advertised;

    private (int PhysicalWidth, int PhysicalHeight, OutputMode Mode, int Scale, OutputTransform Transform, string Description) Snapshot()
    {
        var (physicalWidth, physicalHeight) = _output.PhysicalSize;
        return (physicalWidth, physicalHeight, _output.CurrentMode, OutputScaling.CeilScale(_output.Scale), _output.Transform, _output.Description);
    }

    private void OnModesChanged()
    {
        _advertised = Snapshot();
        foreach (var resource in _resources)
        {
            SendState(resource, sendName: false, sendDescription: true);
        }
    }

    private void OnOutputCommitted(OutputStateFields fields)
    {
        var current = Snapshot();
        if (current == _advertised)
        {
            return;
        }

        var descriptionChanged = current.Description != _advertised.Description;
        _advertised = current;
        foreach (var resource in _resources)
        {
            SendState(resource, sendName: false, sendDescription: descriptionChanged);
        }
    }

    private void SendState(WlOutputResource resource, bool sendName, bool sendDescription)
    {
        var (physicalWidth, physicalHeight) = _output.PhysicalSize;
        resource.SendGeometry(
            _position.X,
            _position.Y,
            physicalWidth,
            physicalHeight,
            WlOutput.Subpixel.Unknown,
            _output.Make,
            _output.Model,
            (WlOutput.Transform)_output.Transform);

        var overridden = _overrides.TryGetValue(resource.Client, out var view);
        var mode = overridden ? view.Mode : _output.CurrentMode;
        resource.SendMode(WlOutput.Mode.Current | WlOutput.Mode.Preferred, mode.Width, mode.Height, mode.RefreshMilliHz);

        if (resource.Version >= 2)
        {
            resource.SendScale(overridden ? view.Scale : OutputScaling.CeilScale(_output.Scale));
        }

        if (resource.Version >= 4)
        {
            if (sendName)
            {
                resource.SendName(_output.Name);
            }

            if (sendDescription && _output.Description.Length > 0)
            {
                resource.SendDescription(_output.Description);
            }
        }

        if (resource.Version >= 2)
        {
            resource.SendDone();
        }
    }
}
