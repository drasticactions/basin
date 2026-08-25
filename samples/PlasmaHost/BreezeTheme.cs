using Basin.UI.Avalonia;
using PlasmaHost.Shell;

namespace PlasmaHost;

internal sealed class BreezeTheme
{
    private readonly Dictionary<string, BreezePalette> _palettes = [];
    private readonly Dictionary<string, BreezeBrushes> _brushes = [];

    public BreezeTheme()
    {
        Metrics = BreezeMetrics.Load();
        Shell = new ShellBrushes(PaletteFor(null));
    }

    public event Action? Changed;

    public BreezeMetrics Metrics { get; }

    public ShellBrushes Shell { get; private set; }

    public BreezePalette Default => PaletteFor(null);

    public UIThemeVariant Variant => Default.IsDark ? UIThemeVariant.Dark : UIThemeVariant.Light;

    public BreezePalette PaletteFor(string? palette)
    {
        var key = palette ?? "";
        if (!_palettes.TryGetValue(key, out var colors))
        {
            colors = BreezePalette.Load(palette);
            _palettes[key] = colors;
        }

        return colors;
    }

    public BreezeBrushes BrushesFor(string? palette)
    {
        var key = palette ?? "";
        if (!_brushes.TryGetValue(key, out var brushes))
        {
            brushes = new BreezeBrushes(PaletteFor(palette));
            _brushes[key] = brushes;
        }

        return brushes;
    }

    public void Reload()
    {
        _palettes.Clear();
        _brushes.Clear();
        Shell = new ShellBrushes(PaletteFor(null));
        Changed?.Invoke();
    }
}
