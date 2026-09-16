namespace Basin.Frames.Metacity;

public sealed class MetacityPalette
{
    internal const int StateCount = 8;

    private readonly MetacityColor[] _bg = new MetacityColor[StateCount];
    private readonly MetacityColor[] _fg = new MetacityColor[StateCount];
    private readonly MetacityColor[] _text = new MetacityColor[StateCount];
    private readonly MetacityColor[] _base = new MetacityColor[StateCount];

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

    public Dictionary<string, MetacityColor> Custom { get; } = new(StringComparer.Ordinal);

    public int Version { get; private set; }

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

    public MetacityColor Bg(MetacityStateFlag state) => _bg[(int)state];

    public MetacityColor Fg(MetacityStateFlag state) => _fg[(int)state];

    public MetacityColor Base(MetacityStateFlag state) => _base[(int)state];

    public MetacityColor Text(MetacityStateFlag state) => _text[(int)state];

    public MetacityColor LightOf(MetacityStateFlag state) => Bg(state).Shade(1.3);

    public MetacityColor DarkOf(MetacityStateFlag state) => Bg(state).Shade(0.7);

    public MetacityColor MidOf(MetacityStateFlag state) => LightOf(state).Mean(DarkOf(state));

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
