using Basin.Capabilities;
using Basin.Scene;

namespace Basin.Host;

/// <summary>
/// Gives scene diagnostics for the host's <see cref="OutputDriver"/>. Until a driver exists, the queries return empty results and the watches throw.
/// </summary>
/// <param name="host">The host whose driver is examined.</param>
/// <param name="services">The services that the diagnostics read.</param>
public sealed class HostSceneDiagnostics(BasinHost host, BasinServices services) : ISceneDiagnostics
{
    private SceneDiagnostics? _inner;

    /// <summary>
    /// Gets the number of frame clock sinks. It is -1 when the host has no driver yet or the frame clock is not the default <see cref="Basin.Capabilities.Defaults.FrameClock"/>.
    /// </summary>
    public int ClockSinks => Inner?.ClockSinks ?? -1;

    private SceneDiagnostics? Inner =>
        _inner ??= host.Driver is { } driver
            ? new SceneDiagnostics(driver.Scene, driver.Renderer, host.Loop, driver) { Services = services }
            : null;

    /// <summary>
    /// Describes every node of the scene tree.
    /// </summary>
    /// <returns>The nodes, or an empty list when the host has no driver yet.</returns>
    public IReadOnlyList<SceneNodeInfo> Tree() => Inner?.Tree() ?? [];

    /// <summary>
    /// Describes the recent frames of each output.
    /// </summary>
    /// <returns>The frames, or an empty list when the host has no driver yet.</returns>
    public IReadOnlyList<FrameInfo> Frames() => Inner?.Frames() ?? [];

    /// <summary>
    /// Sends each scene damage event to a sink until the result is disposed.
    /// </summary>
    /// <param name="stacks">Whether each event carries the stack that caused it.</param>
    /// <param name="sink">The function that gets each event.</param>
    /// <returns>The watch. Dispose it to stop.</returns>
    /// <exception cref="InvalidOperationException">The host has no driver yet.</exception>
    public IDisposable WatchDamage(bool stacks, Action<DamageInfo> sink) => Require().WatchDamage(stacks, sink);

    /// <summary>
    /// Sends each frame report to a sink until the result is disposed.
    /// </summary>
    /// <param name="sink">The function that gets each frame.</param>
    /// <returns>The watch. Dispose it to stop.</returns>
    /// <exception cref="InvalidOperationException">The host has no driver yet.</exception>
    public IDisposable WatchFrames(Action<FrameInfo> sink) => Require().WatchFrames(sink);

    /// <summary>
    /// Creates a locator for the window that contains a surface.
    /// </summary>
    /// <param name="surface">The surface to locate.</param>
    /// <param name="margin">The margin in logical pixels around the box.</param>
    /// <returns>A function that gives the current box, or <see langword="null"/> when the host has no driver yet or the surface is not found.</returns>
    public Func<Box?>? LocateWindow(Surface surface, int margin) => Inner?.LocateWindow(surface, margin);

    /// <summary>
    /// Creates a locator for a surface.
    /// </summary>
    /// <param name="surface">The surface to locate.</param>
    /// <param name="margin">The margin in logical pixels around the box.</param>
    /// <returns>A function that gives the current box, or <see langword="null"/> when the host has no driver yet or the surface is not found.</returns>
    public Func<Box?>? LocateSurface(Surface surface, int margin) => Inner?.LocateSurface(surface, margin);

    /// <summary>
    /// Creates a locator for a scene node.
    /// </summary>
    /// <param name="path">The node path in the scene tree.</param>
    /// <param name="margin">The margin in logical pixels around the box.</param>
    /// <param name="error">The reason when the result is <see langword="null"/>.</param>
    /// <returns>A function that gives the current box, or <see langword="null"/> when the node is not found or the host has no driver yet.</returns>
    public Func<Box?>? LocateNode(string path, int margin, out string? error)
    {
        if (Inner is { } inner)
        {
            return inner.LocateNode(path, margin, out error);
        }

        error = "The compositor has no output driver yet. Try again after startup.";
        return null;
    }

    /// <summary>
    /// Starts a recording.
    /// </summary>
    /// <param name="request">What to record.</param>
    /// <param name="done">The function that gets the result when the recording ends.</param>
    /// <returns><see langword="null"/> when the recording started, otherwise the reason that it did not.</returns>
    public string? Record(RecordRequest request, Action<RecordingResult> done) =>
        Inner is { } inner ? inner.Record(request, done) : "The compositor has no output driver yet. Try again after startup.";

    /// <summary>
    /// Reports the frame parts of a surface's window and their boxes.
    /// </summary>
    /// <param name="surface">The surface whose window is examined.</param>
    /// <param name="part">The function that gets each part and its box.</param>
    /// <returns><see langword="false"/> when the host has no driver yet or the surface has no frame.</returns>
    public bool WriteParts(Surface surface, Action<FramePart, Box> part) => Inner?.WriteParts(surface, part) ?? false;

    /// <summary>
    /// Turns plane offload and direct scanout on or off. It does nothing when the host has no driver yet.
    /// </summary>
    /// <param name="allowed">Whether planes are allowed.</param>
    public void AllowPlanes(bool allowed) => Inner?.AllowPlanes(allowed);

    /// <summary>
    /// Describes the plane assignment of each output.
    /// </summary>
    /// <returns>The reports, or an empty list when the host has no driver yet.</returns>
    public IReadOnlyList<PlaneReport> Planes() => Inner?.Planes() ?? [];

    /// <summary>
    /// Sends each plane report to a sink until the result is disposed.
    /// </summary>
    /// <param name="sink">The function that gets each report.</param>
    /// <returns>The watch. Dispose it to stop.</returns>
    /// <exception cref="InvalidOperationException">The host has no driver yet.</exception>
    public IDisposable WatchPlanes(Action<PlaneReport> sink) => Require().WatchPlanes(sink);

    private SceneDiagnostics Require() =>
        Inner ?? throw new InvalidOperationException("The compositor has no output driver yet.");
}
