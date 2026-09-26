namespace Basin.Ipc;

public readonly record struct IpcWorkspace(
    ulong Id,
    string Name,
    bool Active,
    bool Urgent,
    bool Hidden,
    ReadOnlyMemory<uint> Coordinates,
    ReadOnlyMemory<ulong> Members);
