namespace Basin.UI.ImGui;

public readonly ref struct ImGuiFrame
{
    private readonly ImGuiUISurface _surface;

    internal ImGuiFrame(ImGuiUISurface surface, int width, int height, double scale, float deltaTime)
    {
        _surface = surface;
        Width = width;
        Height = height;
        Scale = scale;
        DeltaTime = deltaTime;
    }

    public int Width { get; }

    public int Height { get; }

    public double Scale { get; }

    public float DeltaTime { get; }

    public ImGuiUISurface Surface => _surface;

    public void RequestRefresh(int hz) => _surface.RequestRefresh(hz);
}
