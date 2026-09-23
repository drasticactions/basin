namespace EightWm;

public sealed class TileGroupModel(string name, int count)
{
    public string Name { get; } = name;

    public int Count { get; } = count;

    public override string ToString() => Name;
}
