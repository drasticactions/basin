namespace Basin.Ipc;

public readonly record struct IpcWindowState(
    bool Activated,
    bool Maximized,
    bool Minimized,
    bool Fullscreen,
    bool NoBorder,
    bool SkipTaskbar);
