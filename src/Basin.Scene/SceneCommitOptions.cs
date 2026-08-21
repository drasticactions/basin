using Pixman;

namespace Basin.Scene;

public readonly record struct SceneCommitOptions
{
    private readonly bool _disableDirectScanout;
    private readonly bool _disablePlaneOffload;
    private readonly int _maxOffloadLayers;
    private readonly RenderColor _background;
    private readonly bool _backgroundSet;

    public bool AllowDirectScanout
    {
        get => !_disableDirectScanout;
        init => _disableDirectScanout = !value;
    }

    public bool AllowPlaneOffload
    {
        get => !_disablePlaneOffload;
        init => _disablePlaneOffload = !value;
    }

    public int MaxOffloadLayers
    {
        get => _maxOffloadLayers == 0 ? 4 : _maxOffloadLayers;
        init => _maxOffloadLayers = value;
    }

    public bool DebugDamageTint { get; init; }

    public long TargetPresentNanos { get; init; }

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
