namespace Basin.Frames.Metacity;

/// <summary>
/// The GTK colors that the theme's <c>gtk:</c> color specs resolve against, with one entry for each <see cref="MetacityStateFlag"/>.
/// </summary>
public sealed class MetacityPalette
{
    internal const int StateCount = 8;

    private readonly MetacityColor[] _bg = new MetacityColor[StateCount];
    private readonly MetacityColor[] _fg = new MetacityColor[StateCount];
    private readonly MetacityColor[] _text = new MetacityColor[StateCount];
    private readonly MetacityColor[] _base = new MetacityColor[StateCount];

    /// <summary>
    /// Initializes a new instance of the <see cref="MetacityPalette"/> class with the same colors for every state.
    /// </summary>
    /// <param name="bg">The background color.</param>
    /// <param name="fg">The foreground color.</param>
    /// <param name="base">The base color of entry fields.</param>
    /// <param name="text">The text color of entry fields.</param>
    public MetacityPalette(MetacityColor bg, MetacityColor fg, MetacityColor @base, MetacityColor text)
    {
        for (var i = 0; i < StateCount; i++)
        {
            _bg[i] = bg;
            _fg[i] = fg;
            _base[i] = @base;
            _text[i] = text;
        }
    }

    /// <summary>
    /// Gets a new palette with Adwaita's light colors. Each read makes a new instance.
    /// </summary>
    public static MetacityPalette Light
    {
        get
        {
            var palette = new MetacityPalette(
                MetacityColor.FromHex(0xF6F5F4),
                MetacityColor.FromHex(0x2E3436),
                MetacityColor.FromHex(0xFFFFFF),
                MetacityColor.FromHex(0x000000));
            palette.Set(MetacityStateFlag.Selected, bg: MetacityColor.FromHex(0x3584E4), fg: MetacityColor.FromHex(0xFFFFFF), @base: MetacityColor.FromHex(0x3584E4), text: MetacityColor.FromHex(0xFFFFFF));
            palette.Set(MetacityStateFlag.Insensitive, fg: MetacityColor.FromHex(0x929595), text: MetacityColor.FromHex(0x929595));
            palette.Set(MetacityStateFlag.Backdrop, fg: MetacityColor.FromHex(0x929595), text: MetacityColor.FromHex(0x929595));
            palette.Set(MetacityStateFlag.Prelight, bg: MetacityColor.FromHex(0xFFFFFF));
            palette.Set(MetacityStateFlag.Active, bg: MetacityColor.FromHex(0xD6D1CD));
            return palette;
        }
    }

    /// <summary>
    /// Gets a new palette with Adwaita's dark colors. Each read makes a new instance.
    /// </summary>
    public static MetacityPalette Dark
    {
        get
        {
            var palette = new MetacityPalette(
                MetacityColor.FromHex(0x353535),
                MetacityColor.FromHex(0xEEEEEC),
                MetacityColor.FromHex(0x2D2D2D),
                MetacityColor.FromHex(0xFFFFFF));
            palette.Set(MetacityStateFlag.Selected, bg: MetacityColor.FromHex(0x15539E), fg: MetacityColor.FromHex(0xFFFFFF), @base: MetacityColor.FromHex(0x15539E), text: MetacityColor.FromHex(0xFFFFFF));
            palette.Set(MetacityStateFlag.Insensitive, fg: MetacityColor.FromHex(0x919190), text: MetacityColor.FromHex(0x919190));
            palette.Set(MetacityStateFlag.Backdrop, fg: MetacityColor.FromHex(0x919190), text: MetacityColor.FromHex(0x919190));
            palette.Set(MetacityStateFlag.Prelight, bg: MetacityColor.FromHex(0x3F3F3F));
            palette.Set(MetacityStateFlag.Active, bg: MetacityColor.FromHex(0x282828));
            return palette;
        }
    }

    private readonly Dictionary<string, MetacityColor> _custom = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the named colors for <c>gtk:custom</c> specs. A missing name uses the spec's fallback. Change them with <see cref="SetCustom"/> and <see cref="RemoveCustom"/>.
    /// </summary>
    public IReadOnlyDictionary<string, MetacityColor> Custom => _custom;

    /// <summary>
    /// Gets a counter that each <see cref="Set"/>, <see cref="SetCustom"/> and <see cref="RemoveCustom"/> call increments. A painter resolves its colors again when it changes.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Sets a named color for <c>gtk:custom</c> specs and increments <see cref="Version"/>.
    /// </summary>
    /// <param name="name">The color name.</param>
    /// <param name="color">The color.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public void SetCustom(string name, MetacityColor color)
    {
        ArgumentNullException.ThrowIfNull(name);
        _custom[name] = color;
        Version++;
    }

    /// <summary>
    /// Removes a named color, so <c>gtk:custom</c> specs with that name use their fallback. It increments <see cref="Version"/> when the name was set.
    /// </summary>
    /// <param name="name">The color name.</param>
    /// <returns><see langword="true"/> when the name was set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool RemoveCustom(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_custom.Remove(name))
        {
            return false;
        }

        Version++;
        return true;
    }

    /// <summary>
    /// Sets the colors for one state. A <see langword="null"/> argument keeps the current color.
    /// </summary>
    /// <param name="state">The state to change.</param>
    /// <param name="bg">The new background color.</param>
    /// <param name="fg">The new foreground color.</param>
    /// <param name="base">The new base color.</param>
    /// <param name="text">The new text color.</param>
    public void Set(MetacityStateFlag state, MetacityColor? bg = null, MetacityColor? fg = null, MetacityColor? @base = null, MetacityColor? text = null)
    {
        var i = (int)state;
        if (bg is { } b)
        {
            _bg[i] = b;
        }

        if (fg is { } f)
        {
            _fg[i] = f;
        }

        if (@base is { } bs)
        {
            _base[i] = bs;
        }

        if (text is { } t)
        {
            _text[i] = t;
        }

        Version++;
    }

    /// <summary>
    /// Gets the background color, GTK's <c>bg</c>.
    /// </summary>
    /// <param name="state">The widget state.</param>
    /// <returns>The color for the state.</returns>
    public MetacityColor Bg(MetacityStateFlag state) => _bg[(int)state];

    /// <summary>
    /// Gets the foreground color, GTK's <c>fg</c>.
    /// </summary>
    /// <param name="state">The widget state.</param>
    /// <returns>The color for the state.</returns>
    public MetacityColor Fg(MetacityStateFlag state) => _fg[(int)state];

    /// <summary>
    /// Gets the base color of entry fields, GTK's <c>base</c>.
    /// </summary>
    /// <param name="state">The widget state.</param>
    /// <returns>The color for the state.</returns>
    public MetacityColor Base(MetacityStateFlag state) => _base[(int)state];

    /// <summary>
    /// Gets the text color of entry fields, GTK's <c>text</c>.
    /// </summary>
    /// <param name="state">The widget state.</param>
    /// <returns>The color for the state.</returns>
    public MetacityColor Text(MetacityStateFlag state) => _text[(int)state];

    /// <summary>
    /// Gets GTK's <c>light</c> color, the background shaded by 1.3.
    /// </summary>
    /// <param name="state">The widget state.</param>
    /// <returns>The derived color.</returns>
    public MetacityColor LightOf(MetacityStateFlag state) => Bg(state).Shade(1.3);

    /// <summary>
    /// Gets GTK's <c>dark</c> color, the background shaded by 0.7.
    /// </summary>
    /// <param name="state">The widget state.</param>
    /// <returns>The derived color.</returns>
    public MetacityColor DarkOf(MetacityStateFlag state) => Bg(state).Shade(0.7);

    /// <summary>
    /// Gets GTK's <c>mid</c> color, the mean of the light and dark colors.
    /// </summary>
    /// <param name="state">The widget state.</param>
    /// <returns>The derived color.</returns>
    public MetacityColor MidOf(MetacityStateFlag state) => LightOf(state).Mean(DarkOf(state));

    /// <summary>
    /// Gets GTK's <c>text_aa</c> color, the mean of the foreground and base colors.
    /// </summary>
    /// <param name="state">The widget state.</param>
    /// <returns>The derived color.</returns>
    public MetacityColor TextAaOf(MetacityStateFlag state) => Fg(state).Mean(Base(state));

    internal MetacityColor Component(MetacityGtkComponent component, MetacityStateFlag state) => component switch
    {
        MetacityGtkComponent.Fg => Fg(state),
        MetacityGtkComponent.Bg => Bg(state),
        MetacityGtkComponent.Light => LightOf(state),
        MetacityGtkComponent.Dark => DarkOf(state),
        MetacityGtkComponent.Mid => MidOf(state),
        MetacityGtkComponent.Text => Text(state),
        MetacityGtkComponent.Base => Base(state),
        _ => TextAaOf(state),
    };
}
