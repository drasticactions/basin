using Basin.Effects;
using Basin.Scene;

namespace PlasmaHost;

internal sealed class PlasmaHostShadows : IDisposable
{
    private const double InactiveStrength = 0.5;

    private readonly DropShadowOptions? _options;
    private readonly List<((double Scale, bool Active) Key, DropShadowTexture? Texture)> _cache = [];

    public PlasmaHostShadows(double cornerRadius)
    {
        _options = BreezeShadow.Load(cornerRadius);
    }

    public bool Enabled => _options is not null;

    public DropShadowEffect? Create(SceneTree parent) =>
        _options is null ? null : new DropShadowEffect(parent);

    public DropShadowTexture? TextureFor(double scale, bool active)
    {
        if (_options is not { } options)
        {
            return null;
        }

        var key = (Math.Round(Math.Max(scale, 0.25), 2), active);
        foreach (var entry in _cache)
        {
            if (entry.Key == key)
            {
                return entry.Texture;
            }
        }

        var texture = DropShadowTexture.Build(
            options with { Strength = options.Strength * (active ? 1 : InactiveStrength) },
            key.Item1);
        _cache.Add((key, texture));
        return texture;
    }

    public void Dispose()
    {
        foreach (var entry in _cache)
        {
            entry.Texture?.Dispose();
        }

        _cache.Clear();
    }
}
