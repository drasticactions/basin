namespace Basin.Ipc;

public readonly record struct IpcDiagRecordFrame(
    int Index, IpcBox At, double Luma, double Peak, int Delta, IpcBox[] Planes, bool Unmapped);
