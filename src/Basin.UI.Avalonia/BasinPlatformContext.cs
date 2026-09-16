using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Basin.Diagnostics;

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

    public IAvaloniaGpu? Gpu { get; internal init; }

    public AvaloniaUIHost? Host { get; internal set; }

    public IScreenImpl? Screens { get; internal set; }

    public ThreadAffinity? Affinity { get; internal init; }

    public bool Attached => Affinity is not null;

    public object? TryGetFeature(Type featureType) => _features(featureType);
}
