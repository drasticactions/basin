using Basin.Capabilities;
using Basin.Frames.Metacity;

namespace TinyComp;

internal sealed class MetacityFrames : IDisposable
{
    private readonly MetacityTheme _theme;
    private readonly MetacityPalette _palette;
    private readonly MetacityButtonLayout _layout;
    private readonly MetacityFont _font;
    private readonly MetacityResources _resources;
    private bool _disposed;

    private MetacityFrames(Config config, FrameTheme frameTheme, MetacityTheme theme)
    {
        _theme = theme;
        ThemeName = config.MetacityTheme!;
        LayoutText = config.MetacityButtonLayout;
        PaletteName = config.MetacityPalette;
        _palette = PaletteName == "dark" ? MetacityPalette.Dark : MetacityPalette.Light;
        _layout = MetacityButtonLayout.Parse(LayoutText);
        _font = new MetacityFont(frameTheme.Typeface, frameTheme.FontSize);
        _resources = new MetacityResources();
    }

    public string ThemeName { get; }

    public string LayoutText { get; }

    public string PaletteName { get; }

    public static MetacityFrames Load(Config config, FrameTheme frameTheme)
    {
        var name = config.MetacityTheme ?? throw new MetacityThemeException("[frame.metacity] theme is not set");
        return new MetacityFrames(config, frameTheme, MetacityTheme.Load(name));
    }

    public bool Matches(Config config) =>
        config.MetacityTheme == ThemeName && config.MetacityButtonLayout == LayoutText && config.MetacityPalette == PaletteName
        && _font.Size == (float)config.FontSize;

    public IFrameRenderer CreateRenderer() => new MetacityFrameRenderer(_theme, _palette, _layout, _font, _resources);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _resources.Dispose();
        _font.Dispose();
    }
}
