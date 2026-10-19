namespace Basin.Ipc;

public readonly record struct IpcDiagFrames(IpcDiagFrame[] Frames, int ClockSinks);
