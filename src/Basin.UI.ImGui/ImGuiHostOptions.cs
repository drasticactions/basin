namespace Basin.UI.ImGui;

/// <summary>
/// The settings that an ImGui host reads once, when it is created.
/// </summary>
public sealed record ImGuiHostOptions
{
    /// <summary>
    /// Gets the size of the shared font atlas, in logical pixels. The default is 15.
    /// </summary>
    public float FontSize { get; init; } = 15f;

    /// <summary>
    /// Gets the monotonic clock in nanoseconds that paces the surfaces. When it is <see langword="null"/>, the host uses <see cref="MonotonicClock.Nanos"/>.
    /// </summary>
    public Func<long>? ClockNanos { get; init; }

    /// <summary>
    /// Gets the interval in milliseconds between frames while a surface animates. The host uses at least 1. The default is 16.
    /// </summary>
    public int FrameIntervalMillis { get; init; } = 16;
}
