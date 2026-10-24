namespace Basin.Capabilities;

/// <summary>
/// Reads the scene and the presented outputs for diagnostics: the node tree, window frames, damage, plane offload and recordings.
/// Every member runs on the compositor thread.
/// </summary>
public interface ISceneDiagnostics
{
    /// <summary>
    /// Takes a snapshot of every scene node, depth first from the root.
    /// </summary>
    /// <returns>One entry for each node. The list is empty when the compositor has no scene yet.</returns>
    IReadOnlyList<SceneNodeInfo> Tree();

    /// <summary>
    /// Takes a snapshot of every window frame in the scene.
    /// </summary>
    /// <returns>One entry for each frame.</returns>
    IReadOnlyList<FrameInfo> Frames();

    /// <summary>
    /// Gets the number of sinks on the frame clock. It is -1 when the clock is not the default one or the compositor has no scene yet.
    /// </summary>
    int ClockSinks { get; }

    /// <summary>
    /// Starts to send each scene damage event to <paramref name="sink"/>.
    /// </summary>
    /// <param name="stacks"><see langword="true"/> to capture a stack trace the first time each node path causes damage.</param>
    /// <param name="sink">The callback for each event.</param>
    /// <returns>A handle that stops the watch when disposed.</returns>
    IDisposable WatchDamage(bool stacks, Action<DamageInfo> sink);

    /// <summary>
    /// Starts to send the state of a window frame to <paramref name="sink"/> each time the scene draws that frame.
    /// </summary>
    /// <param name="sink">The callback for each draw.</param>
    /// <returns>A handle that stops the watch when disposed.</returns>
    IDisposable WatchFrames(Action<FrameInfo> sink);

    /// <summary>
    /// Makes a locator for the frame of the window whose surface is <paramref name="surface"/>.
    /// Without a frame, it gives the result of <see cref="LocateSurface"/>.
    /// </summary>
    /// <param name="surface">The root surface of the window.</param>
    /// <param name="margin">The logical pixels to add on each side. A negative value counts as 0.</param>
    /// <returns>A function that gives the current box in scene coordinates, or <see langword="null"/> after the window goes away. It is <see langword="null"/> when the scene does not show the window.</returns>
    Func<Box?>? LocateWindow(Surface surface, int margin);

    /// <summary>
    /// Makes a locator for the bounds of the scene node that shows <paramref name="surface"/>.
    /// </summary>
    /// <param name="surface">The surface to locate.</param>
    /// <param name="margin">The logical pixels to add on each side. A negative value counts as 0.</param>
    /// <returns>A function that gives the current box in scene coordinates, or <see langword="null"/> while the surface is not shown. It is <see langword="null"/> when the scene does not show the surface.</returns>
    Func<Box?>? LocateSurface(Surface surface, int margin);

    /// <summary>
    /// Makes a locator for the bounds of the one scene node at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">A scene path in the form that <see cref="SceneNodeInfo.Path"/> gives.</param>
    /// <param name="margin">The logical pixels to add on each side. A negative value counts as 0.</param>
    /// <param name="error">The reason that no locator was made, or <see langword="null"/> on success.</param>
    /// <returns>A function that gives the current box in scene coordinates, or <see langword="null"/> when no node or more than one node has the path.</returns>
    Func<Box?>? LocateNode(string path, int margin, out string? error);

    /// <summary>
    /// Starts to record presented frames of a region of one output.
    /// A new recording ends the recording in progress, which then completes as timed out.
    /// </summary>
    /// <param name="request">What to record.</param>
    /// <param name="done">The callback that receives the result when the recording ends. It runs on the compositor thread.</param>
    /// <returns><see langword="null"/> when the recording started, or the reason that it did not start.</returns>
    string? Record(RecordRequest request, Action<RecordingResult> done);

    /// <summary>
    /// Sends the bounds of each part of the frame of a window, in scene coordinates.
    /// </summary>
    /// <param name="surface">The root surface of the window.</param>
    /// <param name="part">The callback for each part, in <see cref="FramePart"/> order.</param>
    /// <returns><see langword="true"/> when the window has a frame.</returns>
    bool WriteParts(Surface surface, Action<FramePart, Box> part);

    /// <summary>
    /// Allows or forbids plane offload on every presented output.
    /// </summary>
    /// <param name="allowed"><see langword="false"/> to composite every node.</param>
    void AllowPlanes(bool allowed);

    /// <summary>
    /// Reports the latest plane offload decisions of each presented output.
    /// </summary>
    /// <returns>One report for each output. The list is empty without presented outputs.</returns>
    IReadOnlyList<PlaneReport> Planes();

    /// <summary>
    /// Starts to send a plane report each time an output presents a frame whose decisions differ from its previous report.
    /// </summary>
    /// <param name="sink">The callback for each changed report.</param>
    /// <returns>A handle that stops the watch when disposed.</returns>
    IDisposable WatchPlanes(Action<PlaneReport> sink);
}
