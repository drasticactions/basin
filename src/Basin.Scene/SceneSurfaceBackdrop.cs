namespace Basin.Scene;

public sealed partial class Scene
{
    private ISurfaceBackdrop? _surfaceBackdrop;

    public ISurfaceBackdrop? SurfaceBackdrop
    {
        get => _surfaceBackdrop;
        set
        {
            if (ReferenceEquals(_surfaceBackdrop, value))
            {
                return;
            }

            var previous = _surfaceBackdrop;
            _surfaceBackdrop = value;
            for (var i = 0; i < _surfaces.Count; i++)
            {
                previous?.Forget(_surfaces[i].Content);
                _surfaces[i].ApplyBackdrop();
            }
        }
    }
}
