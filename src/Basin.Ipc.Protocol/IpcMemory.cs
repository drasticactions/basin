using System.Runtime.InteropServices;

namespace Basin.Ipc;

/// <summary>
/// Turns the <see cref="ReadOnlyMemory{T}"/> lists of the result contracts back into lists.
/// </summary>
public static class IpcMemory
{
    /// <summary>
    /// Gets the items of <paramref name="memory"/> as a list.
    /// When the memory spans a whole array, the result is that array and not a copy.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="memory">The list of a result contract.</param>
    /// <returns>The backing array, or a new array with a copy of the items.</returns>
    public static IReadOnlyList<T> Items<T>(this ReadOnlyMemory<T> memory) =>
        MemoryMarshal.TryGetArray(memory, out var segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length
            ? segment.Array
            : memory.ToArray();
}
