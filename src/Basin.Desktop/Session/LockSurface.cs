using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Holds the lock surface role of one surface on one output. It maps when the client commits a buffer after it acknowledges the first configure.
/// </summary>
public sealed class LockSurface
{
    /// <summary>
    /// The surface role name that a lock surface holds.
    /// </summary>
    public const string RoleName = "ext_session_lock_surface_v1";

    private readonly WlServerDisplay _display;
    private readonly ExtSessionLockSurfaceV1Resource _resource;
    private uint _lastSerial;
    private bool _acked;
    private bool _mapped;

    internal LockSurface(WlServerDisplay display, Surface surface, ExtSessionLockSurfaceV1Resource resource, OutputGlobal output)
    {
        _display = display;
        _resource = resource;
        Surface = surface;
        Output = output;

        resource.AckConfigure += (_, e) =>
        {
            if (e.Serial == _lastSerial)
            {
                _acked = true;
            }
        };
        surface.Committed += OnCommitted;
        surface.Destroyed += () => SetMapped(false);
        resource.Destroyed += (_, _) => SetMapped(false);

        var (logicalWidth, logicalHeight) = output.Output.LogicalSize();
        Configure(logicalWidth, logicalHeight);
    }

    /// <summary>
    /// Gets the surface that has the role.
    /// </summary>
    public Surface Surface { get; }

    /// <summary>
    /// Gets the output that the surface must cover.
    /// </summary>
    public OutputGlobal Output { get; }

    /// <summary>
    /// Gets a value indicating whether the surface has a buffer after an acknowledged configure.
    /// </summary>
    public bool IsMapped => _mapped;

    /// <summary>
    /// Occurs when the surface becomes mapped.
    /// </summary>
    public event Action? Mapped;

    /// <summary>
    /// Occurs when the surface loses its buffer, or when the surface or the role object is destroyed while mapped.
    /// </summary>
    public event Action? Unmapped;

    /// <summary>
    /// Sends a configure with a new serial. It does nothing after the role object is destroyed.
    /// </summary>
    /// <param name="width">The width in logical pixels.</param>
    /// <param name="height">The height in logical pixels.</param>
    public void Configure(int width, int height)
    {
        if (!_resource.IsDestroyed)
        {
            _lastSerial = _display.NextSerial();
            _resource.SendConfigure(_lastSerial, (uint)width, (uint)height);
        }
    }

    private void OnCommitted()
    {
        var hasBuffer = Surface.Current.Buffer is not null;
        if (hasBuffer && !_acked)
        {
            _resource.PostError(
                (uint)ExtSessionLockSurfaceV1.Error.CommitBeforeFirstAck,
                "buffer committed before acking the first configure");
            return;
        }

        SetMapped(hasBuffer);
    }

    private void SetMapped(bool mapped)
    {
        if (_mapped == mapped)
        {
            return;
        }

        _mapped = mapped;
        if (mapped)
        {
            Mapped?.Invoke();
        }
        else
        {
            Unmapped?.Invoke();
        }
    }
}
