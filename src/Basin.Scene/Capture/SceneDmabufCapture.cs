using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// Exports the last composed frame of each tracked <see cref="SceneOutput"/>, or the last buffer reported through <see cref="NotifyPresented"/>, as a dmabuf.
/// It refuses an output while content that is excluded from capture can show on it.
/// </summary>
public sealed class SceneDmabufCapture : IDmabufCapture
{
    private readonly Dictionary<IOutput, SceneOutput> _outputs = [];
    private readonly Dictionary<IOutput, IBuffer> _presented = [];
    private ToplevelInfo[] _scratch = new ToplevelInfo[16];

    /// <summary>
    /// Gets or sets the toplevel model to check for excluded toplevels. <see langword="null"/> checks no toplevels.
    /// </summary>
    public IToplevelModel? Toplevels { get; set; }

    /// <summary>
    /// Gets or sets the scene to check for excluded nodes. <see langword="null"/> checks no nodes.
    /// </summary>
    public Scene? Scene { get; set; }

    /// <summary>
    /// Gets or sets the layout that places the outputs. With a layout, only excluded content that intersects the output refuses its frame.
    /// <see langword="null"/> refuses every output while any excluded content exists.
    /// </summary>
    public OutputLayout? Layout { get; set; }

    /// <summary>
    /// Makes the frames of a scene output available for an output. A second call replaces the first.
    /// </summary>
    /// <param name="output">The output that the frames are for.</param>
    /// <param name="sceneOutput">The scene output that draws the frames.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public void Track(IOutput output, SceneOutput sceneOutput)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(sceneOutput);
        _outputs[output] = sceneOutput;
    }

    /// <summary>
    /// Records the buffer that an output presented when no tracked scene output draws it, for example an output on another card that the whole scene repaints.
    /// The capture exports it until the next call, <see cref="Forget"/>, or the destruction of the buffer.
    /// </summary>
    /// <param name="output">The output that presented the buffer.</param>
    /// <param name="buffer">The presented buffer.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public void NotifyPresented(IOutput output, IBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(buffer);
        _presented[output] = buffer;
    }

    /// <summary>
    /// Stops the export of frames for an output.
    /// </summary>
    /// <param name="output">The output to forget.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public void Forget(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _outputs.Remove(output);
        _presented.Remove(output);
    }

    /// <summary>
    /// Gets the dmabuf of <see cref="SceneOutput.LastTarget"/> for a tracked output, or of the buffer that <see cref="NotifyPresented"/> last reported.
    /// It fails while an excluded toplevel that is not minimized, or an enabled excluded scene node, intersects the output in <see cref="Layout"/>.
    /// Without a layout, or while the scene holds a mirror, it fails while any excluded content exists anywhere.
    /// </summary>
    /// <param name="output">The output to read.</param>
    /// <param name="attributes">The dmabuf planes and format. The buffer keeps ownership of the file descriptors.</param>
    /// <returns><see langword="true"/> when a dmabuf frame exists and no excluded content can show on the output, otherwise <see langword="false"/>.</returns>
    public bool TryCurrentFrame(IOutput output, out DmabufAttributes attributes)
    {
        attributes = default;
        var last = _outputs.TryGetValue(output, out var sceneOutput)
            ? sceneOutput.LastTarget
            : _presented.TryGetValue(output, out var presented) && !presented.IsDestroyed ? presented : null;
        return last is not null
            && !Refuses(output)
            && last.TryGetDmabuf(out attributes);
    }

    internal bool Refuses(IOutput output)
    {
        if (Layout is { } layout && layout.Contains(output) && Scene?.HasMirrors != true)
        {
            var box = layout.BoxOf(output);
            return AnyExcluded(box, global: false) || Scene?.HasCaptureExcluded(box) == true;
        }

        return AnyExcluded(default, global: true) || Scene?.HasCaptureExcluded() == true;
    }

    private bool AnyExcluded(in Box box, bool global)
    {
        if (Toplevels is not { } model)
        {
            return false;
        }

        var count = model.Enumerate(_scratch);
        while (count < 0)
        {
            _scratch = new ToplevelInfo[_scratch.Length * 2];
            count = model.Enumerate(_scratch);
        }

        for (var i = 0; i < count; i++)
        {
            ref readonly var info = ref _scratch[i];
            if ((info.State & ToplevelState.ExcludedFromCapture) == 0)
            {
                continue;
            }

            if (global || ((info.State & ToplevelState.Minimized) == 0 &&
                (info.Geometry.IsEmpty || !info.Geometry.Intersect(box).IsEmpty)))
            {
                return true;
            }
        }

        return false;
    }
}
