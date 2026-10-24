using Basin.Capabilities;
using Basin.Frames.Metacity;
using SkiaSharp;

namespace Basin.Shell.Nested;

/// <summary>
/// The Metacity theme, palette, button layout and title font that the shell draws window frames with.
/// It owns the font and the shared resources until <see cref="Dispose"/>.
/// </summary>
public sealed class MetacityFrames : IDisposable
{
    private readonly MetacityTheme _theme;
    private readonly MetacityPalette _palette;
    private readonly MetacityButtonLayout _layout;
    private readonly MetacityFont _font;
    private readonly MetacityResources _resources;
    private bool _disposed;

    private MetacityFrames(MetacityTheme theme, string themeName, string layoutText, string paletteName, double fontSize)
    {
        _theme = theme;
        ThemeName = themeName;
        LayoutText = layoutText;
        PaletteName = paletteName;
        FontSize = fontSize;
        _palette = paletteName == "dark" ? MetacityPalette.Dark : MetacityPalette.Light;
        _layout = MetacityButtonLayout.Parse(layoutText);
        _font = new MetacityFont(TitleTypeface(), (float)fontSize);
        _resources = new MetacityResources();
    }

    /// <summary>
    /// Gets the theme name from the settings that loaded this instance.
    /// </summary>
    public string ThemeName { get; }

    /// <summary>
    /// Gets the Metacity button layout text, for example <c>menu:minimize,maximize,close</c>.
    /// </summary>
    public string LayoutText { get; }

    /// <summary>
    /// Gets the palette name. The value <c>dark</c> selects the dark palette, and any other value selects the light one.
    /// </summary>
    public string PaletteName { get; }

    /// <summary>
    /// Gets the title font size.
    /// </summary>
    public double FontSize { get; }

    /// <summary>
    /// Loads the frames that <paramref name="settings"/> names.
    /// </summary>
    /// <param name="settings">The settings that give the theme, the button layout, the palette and the font size.</param>
    /// <param name="fallbacks">The theme sources to try, in order, when no installed theme has the name.</param>
    /// <returns>A new instance that the caller must dispose.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> or <paramref name="fallbacks"/> is <see langword="null"/>.</exception>
    /// <exception cref="MetacityThemeException">No installed theme and no fallback has the name.</exception>
    public static MetacityFrames Load(ShellSettings settings, IReadOnlyList<Func<string, MetacityTheme?>> fallbacks)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(fallbacks);
        var theme = LoadTheme(settings.Theme, fallbacks);
        return new MetacityFrames(theme, settings.Theme, settings.ButtonLayout, settings.Palette, settings.FontSize);
    }

    /// <summary>
    /// Loads a theme by name. An installed theme wins. Then each fallback is asked in order, and the first answer wins.
    /// </summary>
    /// <param name="name">The theme name.</param>
    /// <param name="fallbacks">The theme sources to try. Each one returns <see langword="null"/> for a name it does not have.</param>
    /// <returns>The loaded theme.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="fallbacks"/> is <see langword="null"/>.</exception>
    /// <exception cref="MetacityThemeException">No installed theme and no fallback has the name. The message lists the installed themes.</exception>
    public static MetacityTheme LoadTheme(string name, IReadOnlyList<Func<string, MetacityTheme?>> fallbacks)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fallbacks);
        if (MetacityThemes.Find(name) is not null)
        {
            return MetacityTheme.Load(name);
        }

        foreach (var fallback in fallbacks)
        {
            if (fallback(name) is { } found)
            {
                return found;
            }
        }

        throw new MetacityThemeException(
            $"No Metacity theme is named '{name}' ([shell] theme). The installed themes are {Installed()}.");
    }

    /// <summary>
    /// Lists the names of the installed Metacity themes for a message.
    /// </summary>
    /// <returns>The names separated by commas, <c>none</c> when there are none, or <c>unreadable</c> when the theme directories cannot be read.</returns>
    public static string Installed()
    {
        try
        {
            var available = MetacityThemes.Available();
            return available.Count == 0 ? "none" : string.Join(", ", available);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "unreadable";
        }
    }

    /// <summary>
    /// Finds out whether this instance already draws what <paramref name="settings"/> asks for.
    /// </summary>
    /// <param name="settings">The settings to compare.</param>
    /// <returns><see langword="true"/> when the theme, the layout and the palette are equal and the font size differs by less than 0.01.</returns>
    public bool Matches(ShellSettings settings) =>
        settings.Theme == ThemeName && settings.ButtonLayout == LayoutText && settings.Palette == PaletteName
        && Math.Abs(settings.FontSize - FontSize) < 0.01;

    /// <summary>
    /// Creates a frame renderer for one window. The renderer shares the font and the resources of this instance.
    /// </summary>
    /// <returns>A new renderer. It must not be used after this instance is disposed.</returns>
    public IFrameRenderer CreateRenderer() => new MetacityFrameRenderer(_theme, _palette, _layout, _font, _resources);

    /// <summary>
    /// Measures the height of the title text.
    /// </summary>
    /// <param name="scale">The output scale.</param>
    /// <returns>The text height in pixels at <paramref name="scale"/>.</returns>
    public int TitleHeight(double scale) => _font.TextHeight(scale);

    private static SKTypeface TitleTypeface() =>
        SKFontManager.Default.MatchFamily("Sans", SKFontStyle.Bold)
        ?? SKFontManager.Default.MatchFamily(SKTypeface.Default.FamilyName, SKFontStyle.Bold)
        ?? SKTypeface.Default;

    /// <summary>
    /// Releases the font and the shared resources. A second call does nothing.
    /// </summary>
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
