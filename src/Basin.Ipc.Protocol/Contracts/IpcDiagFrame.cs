namespace Basin.Ipc;

public readonly record struct IpcDiagFrame(
    string Name,
    ulong Window,
    long Draws,
    bool Animating,
    bool Visible,
    IpcBox Outer,
    bool Active,
    bool Maximized,
    bool Resizing,
    string Hot,
    string PreviousHot,
    string Pressed,
    bool Inside,
    long NowMs,
    long HoverMs,
    long ActiveMs);
