using Basin;
using Basin.Portal.Client.Protocol;
using Wayland;

namespace Basin.Portal.Client;

/// <summary>
/// Represents one <c>wl_output</c> of the compositor that a portal client connects to.
/// It is read-only: the client observes the state but cannot commit a new one.
/// </summary>
public sealed class ClientOutput : IOutput
{
    private string _pendingName = "";
    private string _pendingDescription = "";
    private OutputMode _pendingMode;
    private double _pendingScale = 1;
    private OutputTransform _pendingTransform;
    private (int Width, int Height) _pendingPhysical;
    private (int X, int Y) _pendingPosition;
    private (int Width, int Height) _pendingLogical;
    private string _pendingMake = "";
    private string _pendingModel = "";

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientOutput"/> class. The instance takes ownership of the proxy.
    /// </summary>
    /// <param name="registryName">The numeric name of the global in the registry.</param>
    /// <param name="proxy">The bound output proxy.</param>
    public ClientOutput(uint registryName, WlOutput proxy)
    {
        RegistryName = registryName;
        Proxy = proxy;
        proxy.Geometry += (_, e) =>
        {
            _pendingPosition = (e.X, e.Y);
            _pendingPhysical = (e.PhysicalWidth, e.PhysicalHeight);
            _pendingTransform = (OutputTransform)(int)e.Transform;
            _pendingMake = e.Make;
            _pendingModel = e.Model;
        };
        proxy.ModeEvent += (_, e) =>
        {
            if ((e.Flags & WlOutput.Mode.Current) != 0)
            {
                _pendingMode = new OutputMode(e.Width, e.Height, e.Refresh);
            }
        };
        proxy.Scale += (_, e) => _pendingScale = Math.Max(1, e.Factor);
        proxy.Name += (_, e) => _pendingName = e.Name;
        proxy.Description += (_, e) => _pendingDescription = e.Description;
        proxy.Done += (_, _) => Apply();
    }

    /// <summary>
    /// Gets the numeric name of the output global. A <c>global_remove</c> event finds the output by it.
    /// </summary>
    public uint RegistryName { get; }

    /// <summary>
    /// Gets the bound <c>wl_output</c> proxy. Capture sources for this output are made from it.
    /// </summary>
    public WlOutput Proxy { get; }

    /// <summary>
    /// Gets the xdg-output object that <see cref="AttachXdgOutput"/> created, or <see langword="null"/> before that call.
    /// </summary>
    public ZxdgOutputV1? XdgOutput { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the output received its first complete state with a valid mode.
    /// </summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// Gets the position of the output in the global layout, in logical pixels when xdg-output is present.
    /// </summary>
    public (int X, int Y) Position { get; private set; }

    /// <summary>
    /// Gets the name that the compositor announced. It is <c>output-</c> and the registry name when the compositor sends no name.
    /// </summary>
    public string Name { get; private set; } = "";

    /// <inheritdoc/>
    public string Description { get; private set; } = "";

    /// <inheritdoc/>
    public string Make { get; private set; } = "";

    /// <inheritdoc/>
    public string Model { get; private set; } = "";

    /// <summary>
    /// Gets an empty string. The client protocol does not carry the serial number.
    /// </summary>
    public string Serial => "";

    /// <inheritdoc/>
    public (int Width, int Height) PhysicalSize { get; private set; }

    /// <summary>
    /// Gets the mode that the compositor marked as current in the last complete state.
    /// </summary>
    public OutputMode CurrentMode { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the output is on. It is always <see langword="true"/>, because the compositor announces only active outputs.
    /// </summary>
    public bool Enabled => true;

    /// <summary>
    /// Gets the scale from logical to physical pixels. With xdg-output it is the mode width divided by the logical width, and it is never below 0.25.
    /// </summary>
    public double Scale { get; private set; } = 1;

    /// <summary>
    /// Gets the transform from the last <c>wl_output.geometry</c> event.
    /// </summary>
    public OutputTransform Transform { get; private set; }

    /// <summary>
    /// Gets a value indicating whether variable refresh is in force. It is always <see langword="false"/>, because the client protocol does not report it.
    /// </summary>
    public bool AdaptiveSync => false;

    /// <summary>
    /// Occurs never. A client-side output has no frame pacing.
    /// </summary>
    public event Action? Frame
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <summary>
    /// Occurs never. <see cref="RequestRepaint"/> does nothing on a client-side output.
    /// </summary>
    public event Action? RepaintRequested
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <summary>
    /// Occurs when a later complete state from the compositor changes the mode, the scale or the transform.
    /// The argument names the fields that changed. The first state raises <see cref="Ready"/> instead.
    /// </summary>
    public event Action<OutputStateFields>? Committed;

    /// <summary>
    /// Occurs once from <see cref="NotifyRemoved"/>, before the proxies are destroyed.
    /// </summary>
    public event Action? Destroyed;

    /// <summary>
    /// Occurs once, when the first complete state with a valid mode arrives.
    /// </summary>
    public event Action? Ready;

    /// <summary>
    /// Gets the xdg-output object for this output and listens for its logical position, size, name and description.
    /// A second call does nothing.
    /// </summary>
    /// <param name="manager">The bound xdg-output manager.</param>
    public void AttachXdgOutput(ZxdgOutputManagerV1 manager)
    {
        if (XdgOutput is not null)
        {
            return;
        }

        var xdg = manager.GetXdgOutput(Proxy);
        XdgOutput = xdg;
        xdg.LogicalPosition += (_, e) => _pendingPosition = (e.X, e.Y);
        xdg.LogicalSize += (_, e) => _pendingLogical = (e.Width, e.Height);
        xdg.Name += (_, e) => _pendingName = e.Name;
        xdg.Description += (_, e) => _pendingDescription = e.Description;
#pragma warning disable CS0618
        if (manager.Version < 3)
        {
            xdg.Done += (_, _) => Apply();
        }
#pragma warning restore CS0618
    }

    /// <summary>
    /// Does nothing. A client-side output has no repaint scheduler.
    /// </summary>
    public void RequestRepaint()
    {
    }

    /// <summary>
    /// Refuses every state, because a client cannot configure the outputs of the compositor.
    /// </summary>
    /// <param name="state">The state to test. It is ignored.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool TestCommit(OutputState state) => false;

    /// <summary>
    /// Refuses every state, because a client cannot configure the outputs of the compositor.
    /// </summary>
    /// <param name="state">The state to apply. It is ignored.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool Commit(OutputState state) => false;

    /// <summary>
    /// Raises <see cref="Destroyed"/> and then destroys the xdg-output object and the output proxy. Call it when the global goes away.
    /// </summary>
    public void NotifyRemoved()
    {
        Destroyed?.Invoke();
        if (XdgOutput is { IsDestroyed: false } xdg)
        {
            xdg.Destroy();
        }

        if (!Proxy.IsDestroyed)
        {
            Proxy.Dispose();
        }
    }

    private void Apply()
    {
        if (_pendingMode.Width <= 0 || _pendingMode.Height <= 0)
        {
            return;
        }

        var scale = _pendingScale;
        if (_pendingLogical.Width > 0)
        {
            var rotated = _pendingTransform is OutputTransform.Rotate90 or OutputTransform.Rotate270 or OutputTransform.Flipped90 or OutputTransform.Flipped270;
            var pixelWidth = rotated ? _pendingMode.Height : _pendingMode.Width;
            scale = Math.Max(0.25, (double)pixelWidth / _pendingLogical.Width);
        }

        var fields = OutputStateFields.None;
        if (CurrentMode != _pendingMode)
        {
            fields |= OutputStateFields.Mode;
        }

        if (Math.Abs(Scale - scale) > 0.0001)
        {
            fields |= OutputStateFields.Scale;
        }

        if (Transform != _pendingTransform)
        {
            fields |= OutputStateFields.Transform;
        }

        if (!IsReady)
        {
            fields |= OutputStateFields.Enabled;
        }

        Name = string.IsNullOrEmpty(_pendingName) ? $"output-{RegistryName}" : _pendingName;
        Description = _pendingDescription;
        Make = _pendingMake;
        Model = _pendingModel;
        PhysicalSize = _pendingPhysical;
        CurrentMode = _pendingMode;
        Scale = scale;
        Transform = _pendingTransform;
        Position = _pendingPosition;
        var first = !IsReady;
        IsReady = true;
        if (first)
        {
            Ready?.Invoke();
        }
        else if (fields != OutputStateFields.None)
        {
            Committed?.Invoke(fields);
        }
    }
}
