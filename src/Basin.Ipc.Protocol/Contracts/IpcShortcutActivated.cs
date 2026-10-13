namespace Basin.Ipc;

public readonly record struct IpcShortcutActivated(string AppId, string Id, bool Pressed, bool NoBlock = false, bool OneShot = false);
