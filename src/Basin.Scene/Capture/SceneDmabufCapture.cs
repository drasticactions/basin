using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// Exports the last composed frame of each tracked <see cref="SceneOutput"/> as a dmabuf.
/// It refuses while any content that is excluded from capture exists.
/// </summary>
public sealed class SceneDmabufCapture : IDmabufCapture
{
    private readonly Dictionary<IOutput, SceneOutput> _outputs = [];
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
    /// Stops the export of frames for an output.
    /// </summary>
    /// <param name="output">The output to forget.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public void Forget(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _outputs.Remove(output);
    }

    /// <summary>
    /// Gets the dmabuf of <see cref="SceneOutput.LastTarget"/> for a tracked output.
    /// It fails while an excluded toplevel or scene node exists anywhere, even off this output.
    /// </summary>
    /// <param name="output">The output to read.</param>
    /// <param name="attributes">The dmabuf planes and format. The buffer keeps ownership of the file descriptors.</param>
    /// <returns><see langword="true"/> when a dmabuf frame exists and nothing is excluded, otherwise <see langword="false"/>.</returns>
    public bool TryCurrentFrame(IOutput output, out DmabufAttributes attributes)
    {
        attributes = default;
        return !AnyExcluded()
            && Scene?.HasCaptureExcluded() != true
            && _outputs.TryGetValue(output, out var sceneOutput)
            && sceneOutput.LastTarget is { } last
            && last.TryGetDmabuf(out attributes);
    }

    private bool AnyExcluded()
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
            if ((_scratch[i].State & ToplevelState.ExcludedFromCapture) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
