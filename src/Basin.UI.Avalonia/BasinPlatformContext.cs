using Avalonia.Platform;
using Avalonia.Rendering.Composition;

namespace Basin.UI.Avalonia;

internal sealed class BasinPlatformContext
{
    private readonly Func<Type, object?> _features;

    public BasinPlatformContext(Compositor compositor, Func<Type, object?> features)
    {
        Compositor = compositor;
        _features = features;
    }

    public Compositor Compositor { get; }

    public AvaloniaUIHost? Host { get; internal set; }

    public IScreenImpl? Screens { get; internal set; }

    public object? TryGetFeature(Type featureType) => _features(featureType);
}
