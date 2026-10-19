namespace Basin.Ipc;

public readonly record struct IpcDiagDrag(
    ulong Window, string Part, double FromX, double FromY, double ToX, double ToY, int Steps, bool Settled, int Frames);
