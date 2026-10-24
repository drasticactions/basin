using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Pixman;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>xdg_system_bell_v1</c> global. Each ring goes to <see cref="IBell"/> and then to <see cref="Rang"/>.
/// </summary>
public sealed class SystemBellManager : IDisposable
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly IBell? _bell;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemBellManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global that resolves the ringing surface.</param>
    /// <param name="bell">The bell to ring, or <see langword="null"/> to raise <see cref="Rang"/> only.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public SystemBellManager(WlServerDisplay display, CompositorGlobal compositor, IBell? bell)
    {
        ArgumentNullException.ThrowIfNull(display);
        _compositor = compositor;
        _bell = bell;
        _global = display.CreateGlobal(XdgSystemBellV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs after each ring. The argument is the surface that the client named, or <see langword="null"/>.
    /// </summary>
    public event Action<Surface?>? Rang;

    /// <summary>
    /// Removes the global. Bound clients keep their resources.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var bell = new XdgSystemBellV1Resource(client, version, id);
        bell.Ring += (_, e) =>
        {
            var surface = e.Surface is { } resource ? _compositor.ResolveSurface(resource) : null;
            _bell?.Ring(surface);
            Rang?.Invoke(surface);
        };
    }
}
