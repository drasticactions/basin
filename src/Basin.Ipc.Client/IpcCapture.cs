using Microsoft.Win32.SafeHandles;

namespace Basin.Ipc;

public sealed record IpcCapture(
    int Width, int Height, string? Path, byte[]? Png, SafeFileHandle? Fd, string? Format, int Stride);
