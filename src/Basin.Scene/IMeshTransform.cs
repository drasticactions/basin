namespace Basin.Scene;

public interface IMeshTransform
{
    Box MapBounds(in Box childBounds);

    int VertexCount(in Box childBounds);

    void WriteVertices(in Box childBounds, Span<MeshVertex> into);
}
