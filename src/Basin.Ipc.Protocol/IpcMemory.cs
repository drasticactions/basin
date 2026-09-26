using System.Runtime.InteropServices;

namespace Basin.Ipc;

public static class IpcMemory
{
    public static IReadOnlyList<T> Items<T>(this ReadOnlyMemory<T> memory) =>
        MemoryMarshal.TryGetArray(memory, out var segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length
            ? segment.Array
            : memory.ToArray();
}
