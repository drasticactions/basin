namespace Basin.Scene;

public interface IInvertibleMeshTransform : IMeshTransform
{
    bool TryMapToSource(in Box childBounds, double x, double y, out double sourceX, out double sourceY);
}
