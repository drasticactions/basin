namespace Basin.Ipc;

public readonly record struct IpcDiagParts(ulong Window, string AppId, string Title, IpcDiagPart[] Parts);
