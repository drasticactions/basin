namespace Basin.UI.ImGui;

public sealed record ImGuiHostOptions
{
    public float FontSize { get; init; } = 15f;

    public Func<long>? ClockNanos { get; init; }

    public int FrameIntervalMillis { get; init; } = 16;
}
