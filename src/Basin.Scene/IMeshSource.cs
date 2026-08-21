namespace Basin.Scene;

public interface IMeshSource
{
    int VertexCount(in Box bounds);

    void WriteVertices(in Box bounds, Span<MeshVertex> into);
}
