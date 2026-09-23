using Avalonia.Media;

namespace EightWm;

public sealed class Tile : ObservableModel
{
    private string? _peek;
    private string? _badge;
    private bool _selected;
    private IImage? _iconImage;
    private int _launches;

    public required string Name { get; init; }

    public required string Exec { get; init; }

    public TileSize Size { get; init; } = TileSize.Square;

    public uint Color { get; init; } = 0xff2d89ef;

    public string Group { get; init; } = "Main";

    public string? Icon { get; init; }

    public string? DesktopId { get; init; }

    public string? PeekCommand { get; init; }

    public string? BadgeCommand { get; init; }

    public int PeekIntervalSeconds { get; init; } = 60;

    public string? Peek
    {
        get => _peek;
        set => Set(ref _peek, value);
    }

    public string? Badge
    {
        get => _badge;
        set => Set(ref _badge, value);
    }

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public IImage? IconImage
    {
        get => _iconImage;
        set => Set(ref _iconImage, value);
    }

    public DateTime Installed { get; init; }

    public int Launches
    {
        get => _launches;
        set => Set(ref _launches, value);
    }

    public IBrush Fill => new SolidColorBrush(Avalonia.Media.Color.FromUInt32(Color));

    public double Width => SpanOf(Size).Width - (2 * Margin);

    public double Height => SpanOf(Size).Height - (2 * Margin);

    public double IconSize => Math.Round(Math.Min(Width, Height - LabelHeight) * 0.5);

    public double IconLift => LabelHeight * 0.4;

    public const double Cell = 80;

    public const double Margin = 5;

    public const double LabelHeight = 26;

    internal long NextPollMillis { get; set; }

    public static (double Width, double Height) SpanOf(TileSize size) => size switch
    {
        TileSize.Small => (Cell, Cell),
        TileSize.Wide => (4 * Cell, 2 * Cell),
        TileSize.Large => (4 * Cell, 4 * Cell),
        _ => (2 * Cell, 2 * Cell),
    };
}
