using Basin.Capabilities;
using Basin.Frames.MsStyles;
using Basin.Frames.MsStyles.Quill;
using Basin.Render.Skia;
using Basin.Themes.MsStyles;
using SkiaSharp;

namespace TinyComp;

internal sealed class MsStylesFrames : IDisposable
{
    private readonly MsStylesTheme _theme;
    private readonly MsStylesFrameStyle _style;
    private readonly SKTypeface? _fallback;
    private readonly MsStylesFonts _fonts;
    private readonly MsStylesResources _resources;
    private readonly Func<string, int, SKImage?>? _icons;
    private MsStylesQuillPainter? _quill;
    private MsStylesQuillText? _quillText;
    private bool _disposed;

    private MsStylesFrames(Config config, string path, MsStylesTheme theme, MsStylesFrameStyle style, SKTypeface? fallback, Func<string, int, SKImage?>? icons)
    {
        ThemeName = config.MsStylesTheme!;
        ThemePath = path;
        ColorName = config.MsStylesColor;
        SizeName = config.MsStylesSize;
        LookName = config.MsStylesLook;
        FontName = config.MsStylesFont;
        Colorization = config.MsStylesColorization;
        Frosted = config.MsStylesBackdropBlur > 0;
        Painter = config.MsStylesPainter;
        _theme = theme;
        _style = style;
        _fallback = fallback;
        _icons = icons;
        _fonts = new MsStylesFonts(fallback);
        _resources = new MsStylesResources(style.Images) { Text = new MsStylesSkiaText(_fonts), Icons = icons };
    }

    public string ThemeName { get; }

    public string ThemePath { get; }

    public string ColorName { get; }

    public string SizeName { get; }

    public string LookName { get; }

    public string FontName { get; }

    public uint Colorization { get; }

    public bool Frosted { get; }

    public string Painter { get; }

    public bool Glass => _style.IsGlass;

    public string Describe() =>
        $"MSSTYLES theme={ThemeName} path={ThemePath} format={_theme.Format} color={_theme.Color} size={_theme.Size} look={LookOf(_style.Look)} painter={Painter}";

    public static MsStylesFrames Load(Config config, Func<string, int, SKImage?>? icons)
    {
        var name = config.MsStylesTheme ?? throw new MsStylesFrameException("[frame.msstyles] theme is not set. Set it to an installed visual style.");
        var path = MsStylesThemes.Find(name) ?? throw new MsStylesFrameException($"No msstyles theme named {name} is in ~/.themes, the msstyles data directories or the Wine prefix.");
        MsStylesTheme theme;
        MsStylesFrameStyle style;
        try
        {
            theme = MsStylesTheme.Load(path, Empty(config.MsStylesColor), Empty(config.MsStylesSize));
            style = new MsStylesFrameStyle(theme, Look(config.MsStylesLook), config.MsStylesColorization);
        }
        catch (Exception e) when (e is MsStylesFormatException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw new MsStylesFrameException(e.Message, e);
        }

        SKTypeface? fallback = null;
        if (config.MsStylesFont is { Length: > 0 } family)
        {
            fallback = SKFontManager.Default.MatchFamily(family);
            if (fallback is null)
            {
                throw new MsStylesFrameException($"[frame.msstyles] font = \"{family}\" matches no installed font family. Install it or choose another font.");
            }

            SkiaCensus.Track(fallback);
        }

        return new MsStylesFrames(config, path, theme, style, fallback, icons);
    }

    public bool Matches(Config config) =>
        config.MsStylesTheme == ThemeName && MsStylesThemes.Find(ThemeName) == ThemePath
        && config.MsStylesColor == ColorName && config.MsStylesSize == SizeName && config.MsStylesLook == LookName
        && config.MsStylesFont == FontName && config.MsStylesColorization == Colorization
        && (config.MsStylesBackdropBlur > 0) == Frosted && config.MsStylesPainter == Painter;

    public IFrameRenderer CreateRenderer(bool quillHost)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (quillHost && Painter == "quill")
        {
            _quillText ??= new MsStylesQuillText(_fonts);
            _quill ??= new MsStylesQuillPainter(_style.Images, _quillText) { Icons = _icons };
            return new MsStylesQuillFrameRenderer(_style, _quill);
        }

        return new MsStylesFrameRenderer(_style, _resources);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _resources.Text?.Dispose();
        _quillText?.Dispose();
        _resources.Dispose();
        _fonts.Dispose();
        SkiaCensus.Release(_fallback);
    }

    private static string? Empty(string value) => value.Length == 0 ? null : value;

    private static MsStylesLook Look(string look) => look switch
    {
        "classic-xp" => MsStylesLook.ClassicXp,
        "basic" => MsStylesLook.Basic,
        "glass" => MsStylesLook.Glass,
        _ => MsStylesLook.Auto,
    };

    private static string LookOf(MsStylesLook look) => look switch
    {
        MsStylesLook.ClassicXp => "classic-xp",
        MsStylesLook.Basic => "basic",
        MsStylesLook.Glass => "glass",
        _ => "auto",
    };
}
