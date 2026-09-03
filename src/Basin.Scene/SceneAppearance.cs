using Basin.Capabilities;

namespace Basin.Scene;

public sealed partial class Scene
{
    private readonly List<SceneSurface> _surfaces = [];
    private ISurfaceAppearance? _appearance;
    private AppearanceWatch? _appearanceWatch;

    public ISurfaceAppearance? Appearance
    {
        get => _appearance;
        set
        {
            if (ReferenceEquals(_appearance, value))
            {
                return;
            }

            if (_appearance is { } previous && _appearanceWatch is { } watch)
            {
                previous.RemoveObserver(watch);
            }

            _appearance = value;
            if (value is not null)
            {
                _appearanceWatch ??= new AppearanceWatch(this);
                value.AddObserver(_appearanceWatch);
            }

            for (var i = 0; i < _surfaces.Count; i++)
            {
                _surfaces[i].ApplyAppearance();
            }
        }
    }

    internal void Register(SceneSurface surface) => _surfaces.Add(surface);

    internal void Unregister(SceneSurface surface) => _surfaces.Remove(surface);

    private void OnAppearanceChanged(Surface surface)
    {
        for (var i = 0; i < _surfaces.Count; i++)
        {
            if (ReferenceEquals(_surfaces[i].Surface, surface))
            {
                _surfaces[i].ApplyAppearance();
            }
        }
    }

    private sealed class AppearanceWatch(Scene scene) : ISurfaceAppearanceObserver
    {
        public void AppearanceChanged(Surface surface) => scene.OnAppearanceChanged(surface);
    }
}
