using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class LockSurface
{
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

        Configure(output.Output.CurrentMode.Width, output.Output.CurrentMode.Height);
    }

    public Surface Surface { get; }

    public OutputGlobal Output { get; }

    public bool IsMapped => _mapped;

    public event Action? Mapped;

    public event Action? Unmapped;

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
