using Basin;

namespace EightWm;

internal sealed class Tile
{
    public required string Name { get; init; }

    public required string Exec { get; init; }

    public TileSize Size { get; init; } = TileSize.Square;

    public uint Color { get; init; } = 0xff2d89ef;

    public string Group { get; init; } = "Main";

    public string? Icon { get; init; }

    public string? PeekCommand { get; init; }

    public string? BadgeCommand { get; init; }

    public int PeekIntervalSeconds { get; init; } = 60;

    public Box Box { get; set; }

    public string? Peek { get; set; }

    public string? Badge { get; set; }

    public long NextPollMillis { get; set; }

    public Tween Press;

    public Tween Check;

    public bool Selected { get; set; }

    public double DragX { get; set; }

    public double DragY { get; set; }

    public static (int Width, int Height) UnitsOf(TileSize size) => size switch
    {
        TileSize.Small => (1, 1),
        TileSize.Wide => (4, 2),
        TileSize.Large => (4, 4),
        _ => (2, 2),
    };
}
