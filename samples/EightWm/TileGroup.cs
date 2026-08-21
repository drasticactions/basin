using Basin;

namespace EightWm;

internal sealed class TileGroup
{
    public required string Name { get; init; }

    public List<Tile> Tiles { get; } = [];

    public Box Box { get; set; }
}
