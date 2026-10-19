namespace Basin.Capabilities;

public readonly record struct FrameInfo(
    string Name,
    Surface? Window,
    long Draws,
    bool Animating,
    bool Visible,
    Box Outer,
    bool Active,
    bool Maximized,
    bool Resizing,
    FramePart Hot,
    FramePart PreviousHot,
    FramePart Pressed,
    bool Inside,
    long NowMs,
    long HoverMs,
    long ActiveMs);
