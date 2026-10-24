using Pixman;

namespace Basin.Scene;

/// <summary>
/// Controls one <see cref="SceneOutput"/> commit. The default value allows direct scanout and plane offload, with a black background.
/// </summary>
public readonly record struct SceneCommitOptions
{
    private readonly bool _disableDirectScanout;
    private readonly bool _disablePlaneOffload;
    private readonly int _maxOffloadLayers;
    private readonly RenderColor _background;
    private readonly bool _backgroundSet;

    /// <summary>
    /// Gets a value indicating whether a single full-screen client buffer can go to the primary plane without composition. The default is <see langword="true"/>.
    /// </summary>
    public bool AllowDirectScanout
    {
        get => !_disableDirectScanout;
        init => _disableDirectScanout = !value;
    }

    /// <summary>
    /// Gets a value indicating whether client buffers can go to overlay planes. The default is <see langword="true"/>.
    /// </summary>
    public bool AllowPlaneOffload
    {
        get => !_disablePlaneOffload;
        init => _disablePlaneOffload = !value;
    }

    /// <summary>
    /// Gets the most buffers that the commit offers to overlay planes. Zero means the default of 4.
    /// </summary>
    public int MaxOffloadLayers
    {
        get => _maxOffloadLayers == 0 ? 4 : _maxOffloadLayers;
        init => _maxOffloadLayers = value;
    }

    /// <summary>
    /// Gets a value indicating whether the commit tints the damaged area red, to show what was repainted.
    /// </summary>
    public bool DebugDamageTint { get; init; }

    /// <summary>
    /// Gets the time at which the frame is expected on screen, in nanoseconds on the monotonic clock. Zero means unknown.
    /// </summary>
    public long TargetPresentNanos { get; init; }

    /// <summary>
    /// Gets the color behind the scene. The default is <see cref="RenderColor.Black"/>.
    /// </summary>
    public RenderColor Background
    {
        get => _backgroundSet ? _background : RenderColor.Black;
        init
        {
            _background = value;
            _backgroundSet = true;
        }
    }
}
