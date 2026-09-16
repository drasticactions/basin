namespace Basin.Frames.Metacity;

internal sealed class MetacityDrawOpList
{
    public List<MetacityDrawOp> Ops { get; } = [];

    public bool Contains(MetacityDrawOpList child)
    {
        foreach (var op in Ops)
        {
            if (op.Kind is MetacityDrawOpKind.OpList or MetacityDrawOpKind.Tile && op.OpList is { } nested)
            {
                if (ReferenceEquals(nested, child) || nested.Contains(child))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
