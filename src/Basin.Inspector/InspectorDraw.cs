using Basin.UI.ImGui;

namespace Basin.Inspector;

public ref struct InspectorDraw
{
    private readonly ImGuiFrame _frame;

    internal InspectorDraw(ImGuiFrame frame, InspectorText text, InspectorOverlay overlay, long nowNanos)
    {
        _frame = frame;
        Text = text;
        Overlay = overlay;
        NowNanos = nowNanos;
    }

    public InspectorText Text { get; }

    public InspectorOverlay Overlay { get; }

    public long NowNanos { get; }

    public readonly int Width => _frame.Width;

    public readonly int Height => _frame.Height;

    public readonly double Scale => _frame.Scale;

    public readonly float DeltaTime => _frame.DeltaTime;

    public readonly void RequestRefresh(int hz) => _frame.RequestRefresh(hz);
}
