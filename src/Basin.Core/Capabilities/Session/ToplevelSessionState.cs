using Wayland.Server;

namespace Basin.Capabilities;

/// <summary>
/// Holds the saved state of one toplevel in a session.
/// </summary>
public readonly record struct ToplevelSessionState
{
    /// <summary>
    /// Gets the window geometry in layout coordinates at the time of the save.
    /// </summary>
    public Box Geometry { get; init; }

    /// <summary>
    /// Gets an identifier of the output layout at the time of the save, or <see langword="null"/> when it is not known.
    /// </summary>
    public string? OutputLayoutId { get; init; }

    /// <summary>
    /// Gets the maximized, fullscreen and tiled states at the time of the save.
    /// </summary>
    public ToplevelSessionStates States { get; init; }

    /// <summary>
    /// Gets the name of the workspace that held the window, or <see langword="null"/> for none.
    /// </summary>
    public string? WorkspaceName { get; init; }

    /// <summary>
    /// Gets opaque data that the compositor stores with the state. A store does not have to persist it.
    /// </summary>
    public ReadOnlyMemory<byte> ConsumerData { get; init; }

    /// <summary>
    /// Gets a value that tells whether the saved position is still meaningful in the current output layout.
    /// </summary>
    /// <param name="currentLayoutId">The identifier of the current output layout.</param>
    /// <returns><see langword="true"/> when the saved layout identifier is equal to <paramref name="currentLayoutId"/>, otherwise <see langword="false"/>.</returns>
    public bool CanRestorePosition(string currentLayoutId) =>
        OutputLayoutId is { } saved && saved == currentLayoutId;
}
