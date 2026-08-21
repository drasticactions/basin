using Basin;

namespace EightWm;

internal sealed class TileGrid
{
    public const int Unit = 70;
    public const int Gap = 10;
    public const int GroupGap = 80;

    public List<TileGroup> Groups { get; } = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Rows { get; private set; }

    public void Add(Tile tile)
    {
        var group = Groups.Find(candidate => candidate.Name == tile.Group);
        if (group is null)
        {
            group = new TileGroup { Name = tile.Group };
            Groups.Add(group);
        }

        group.Tiles.Add(tile);
    }

    public void Clear() => Groups.Clear();

    public Tile? At(double x, double y)
    {
        foreach (var group in Groups)
        {
            foreach (var tile in group.Tiles)
            {
                var box = tile.Box;
                if (x >= box.X && y >= box.Y && x < box.Right && y < box.Bottom)
                {
                    return tile;
                }
            }
        }

        return null;
    }

    private int[] _heights = new int[64];

    public void Layout(int availableHeight)
    {
        const int step = Unit + Gap;
        const int gap = Gap;
        Rows = Math.Max(2, (availableHeight + gap) / step);

        var originX = 0;
        var tallest = 0;
        foreach (var group in Groups)
        {
            Array.Clear(_heights);
            var columns = 0;
            var groupHeight = 0;
            foreach (var tile in group.Tiles)
            {
                var (wide, high) = Tile.UnitsOf(tile.Size);
                high = Math.Min(high, Rows);
                var column = 0;
                int top;
                while (true)
                {
                    if (column + wide > _heights.Length)
                    {
                        Array.Resize(ref _heights, _heights.Length * 2);
                    }

                    top = 0;
                    for (var i = column; i < column + wide; i++)
                    {
                        top = Math.Max(top, _heights[i]);
                    }

                    if (top + high <= Rows)
                    {
                        break;
                    }

                    column++;
                }

                for (var i = column; i < column + wide; i++)
                {
                    _heights[i] = top + high;
                }

                columns = Math.Max(columns, column + wide);
                tile.Box = new Box(
                    originX + (column * step), top * step, (wide * step) - gap, (high * step) - gap);
                groupHeight = Math.Max(groupHeight, tile.Box.Bottom);
            }

            var groupWidth = Math.Max(0, (columns * step) - gap);
            group.Box = new Box(originX, 0, groupWidth, groupHeight);
            tallest = Math.Max(tallest, groupHeight);
            originX += groupWidth + GroupGap;
        }

        Width = Math.Max(0, originX - GroupGap);
        Height = tallest;
    }

    public int GroupCount => Groups.Count;
}
