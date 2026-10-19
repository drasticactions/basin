namespace Basin.Inspector;

public interface IInspectorPanel
{
    ReadOnlySpan<byte> Name { get; }

    void Draw(ref InspectorDraw draw);
}
