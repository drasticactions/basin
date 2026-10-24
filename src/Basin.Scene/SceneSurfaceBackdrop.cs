namespace Basin.Scene;

/// <content>
/// Surface backdrop support for the scene.
/// </content>
public sealed partial class Scene
{
    private ISurfaceBackdrop? _surfaceBackdrop;

    /// <summary>
    /// Gets or sets the source of backdrop effects behind surfaces. Setting it makes the previous source forget each surface and re-applies the backdrop.
    /// </summary>
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
