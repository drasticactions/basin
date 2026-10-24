using Basin.Shell.Weston.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Weston;

/// <summary>
/// The <c>weston_desktop_shell</c> global. It forwards the requests of one bound shell client to <see cref="IShellRoles"/> and sends <see cref="IShellClient"/> events back to it.
/// </summary>
public sealed class WestonDesktopShellGlobal : IShellClient, IDisposable
{
    /// <summary>
    /// The version of <c>weston_desktop_shell</c> that the global advertises.
    /// </summary>
    public const int Version = 1;

    private const uint ErrorInvalidArgument = 0;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly IShellRoles _roles;
    private readonly Func<int, bool>? _isPrivileged;
    private WestonDesktopShellResource? _bound;

    /// <summary>
    /// Initializes a new instance of the <see cref="WestonDesktopShellGlobal"/> class. It creates the global on <paramref name="display"/> at once.
    /// </summary>
    /// <param name="display">The display that advertises the global.</param>
    /// <param name="compositor">The compositor global that resolves the client's surfaces.</param>
    /// <param name="roles">The shell policy that receives the requests.</param>
    /// <param name="isPrivileged">The test that gets a client process ID and returns <see langword="true"/> when that client can bind. With <see langword="null"/>, any client can bind. A refused bind gets a protocol error.</param>
    public WestonDesktopShellGlobal(
        WlServerDisplay display,
        CompositorGlobal compositor,
        IShellRoles roles,
        Func<int, bool>? isPrivileged = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(roles);
        _compositor = compositor;
        _roles = roles;
        _isPrivileged = isPrivileged;
        _global = display.CreateGlobal(WestonDesktopShell.Interface, Version, OnBind);
    }

    /// <summary>
    /// Gets a value indicating whether a shell client is bound now. Only one client can bind at a time, and a second bind gets a protocol error.
    /// </summary>
    public bool HasClient => _bound is { IsDestroyed: false };

    /// <inheritdoc/>
    public void Configure(Surface surface, uint edges, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (_bound is { IsDestroyed: false } shell)
        {
            shell.SendConfigure(edges, surface.Resource, width, height);
        }
    }

    /// <inheritdoc/>
    public void PrepareLockSurface()
    {
        if (_bound is { IsDestroyed: false } shell)
        {
            shell.SendPrepareLockSurface();
        }
    }

    /// <inheritdoc/>
    public void GrabCursor(ShellGrabCursor cursor)
    {
        if (_bound is { IsDestroyed: false } shell)
        {
            shell.SendGrabCursor((uint)cursor);
        }
    }

    /// <summary>
    /// Destroys the global. Clients that are bound keep their resources until they disconnect.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var shell = new WestonDesktopShellResource(client, version, id);
        if (!IsAllowed(client))
        {
            shell.PostError(ErrorInvalidArgument, "weston_desktop_shell is reserved for the compositor's own shell client");
            return;
        }

        if (_bound is { IsDestroyed: false })
        {
            shell.PostError(ErrorInvalidArgument, "weston_desktop_shell is already bound");
            return;
        }

        _bound = shell;
        shell.Destroyed += (_, _) =>
        {
            if (ReferenceEquals(_bound, shell))
            {
                _bound = null;
            }
        };

        shell.SetBackground += (_, e) =>
        {
            if (Resolve(shell, e.Output, e.Surface) is { } role)
            {
                _roles.SetBackground(role.Output, role.Surface);
            }
        };

        shell.SetPanel += (_, e) =>
        {
            if (Resolve(shell, e.Output, e.Surface) is { } role)
            {
                _roles.SetPanel(role.Output, role.Surface);
            }
        };

        shell.SetPanelPosition += (_, e) =>
        {
            if (e.Position > (uint)ShellPanelPosition.Right)
            {
                shell.PostError(ErrorInvalidArgument, "unknown panel position");
                return;
            }

            _roles.SetPanelPosition((ShellPanelPosition)e.Position);
        };

        shell.SetLockSurface += (_, e) =>
        {
            if (ResolveSurface(shell, e.Surface) is { } surface)
            {
                _roles.SetLockSurface(surface);
            }
        };

        shell.SetGrabSurface += (_, e) =>
        {
            if (ResolveSurface(shell, e.Surface) is { } surface)
            {
                _roles.SetGrabSurface(surface);
            }
        };

        shell.Unlock += (_, _) => _roles.Unlock();
        shell.DesktopReady += (_, _) => _roles.DesktopReady();
    }

    private bool IsAllowed(WlClient client)
    {
        if (_isPrivileged is null)
        {
            return true;
        }

        return client.TryGetCredentials(out var credentials) && _isPrivileged(credentials.Pid);
    }

    private (IOutput Output, Surface Surface)? Resolve(
        WestonDesktopShellResource shell,
        WlOutputResource? outputResource,
        WlSurfaceResource? surfaceResource)
    {
        var output = OutputGlobal.FromResource(outputResource)?.Output;
        var surface = _compositor.ResolveSurface(surfaceResource);
        if (output is null || surface is null)
        {
            shell.PostError(ErrorInvalidArgument, "unknown output or surface");
            return null;
        }

        return (output, surface);
    }

    private Surface? ResolveSurface(WestonDesktopShellResource shell, WlSurfaceResource? surfaceResource)
    {
        var surface = _compositor.ResolveSurface(surfaceResource);
        if (surface is null)
        {
            shell.PostError(ErrorInvalidArgument, "unknown surface");
        }

        return surface;
    }
}
