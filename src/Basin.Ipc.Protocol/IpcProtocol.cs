using System.Buffers.Binary;

namespace Basin.Ipc;

/// <summary>
/// The framing of the control socket and the rules for its socket path and its method and event names.
/// </summary>
public static class IpcProtocol
{
    /// <summary>
    /// The protocol version that <c>ipc/version</c> reports.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// The size of the frame header: a little-endian unsigned length of the JSON body that follows.
    /// </summary>
    public const int HeaderBytes = 4;

    /// <summary>
    /// The largest request body, 1 MiB. The server closes a connection whose request header is larger.
    /// </summary>
    public const int MaxRequestBytes = 1024 * 1024;

    /// <summary>
    /// The largest reply or event body, 32 MiB. A reply that does not fit is <see cref="IpcErrorCodes.TooLarge"/>.
    /// </summary>
    public const int MaxMessageBytes = 32 * 1024 * 1024;

    /// <summary>
    /// The environment variable that holds the socket path of the compositor for its children.
    /// </summary>
    public const string SocketVariable = "BASIN_SOCKET";

    /// <summary>
    /// The environment variable that names the socket path from outside when the program asks for no path. The server ignores a relative value.
    /// </summary>
    public const string PathVariable = "BASIN_IPC_PATH";

    /// <summary>
    /// The start of the socket file name in the runtime directory.
    /// </summary>
    public const string SocketPrefix = "basin-";

    /// <summary>
    /// The end of the socket file name in the runtime directory.
    /// </summary>
    public const string SocketSuffix = ".sock";

    /// <summary>
    /// Reads the body length from a frame header.
    /// </summary>
    /// <param name="header">At least <see cref="HeaderBytes"/> bytes.</param>
    /// <returns>The length of the body in bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="header"/> is shorter than <see cref="HeaderBytes"/>.</exception>
    public static uint ReadLength(ReadOnlySpan<byte> header) => BinaryPrimitives.ReadUInt32LittleEndian(header);

    /// <summary>
    /// Writes a body length into a frame header.
    /// </summary>
    /// <param name="header">At least <see cref="HeaderBytes"/> bytes.</param>
    /// <param name="length">The length of the body in bytes.</param>
    /// <exception cref="OverflowException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="header"/> is shorter than <see cref="HeaderBytes"/>.</exception>
    public static void WriteLength(Span<byte> header, int length) =>
        BinaryPrimitives.WriteUInt32LittleEndian(header, checked((uint)length));

    /// <summary>
    /// Makes the socket path <c>basin-NAME.sock</c> in the runtime directory.
    /// </summary>
    /// <param name="runtimeDirectory">The value of <c>XDG_RUNTIME_DIR</c>.</param>
    /// <param name="name">The Wayland socket name, or the process id when the socket has no name.</param>
    /// <returns>The path, or <see langword="null"/> when <paramref name="runtimeDirectory"/> is null or empty.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public static string? SocketPath(string? runtimeDirectory, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (string.IsNullOrEmpty(runtimeDirectory))
        {
            return null;
        }

        return Path.Combine(runtimeDirectory, SocketPrefix + name + SocketSuffix);
    }

    /// <summary>
    /// Checks that a method or event name is <c>namespace/verb</c>, with each part made of <c>a</c> to <c>z</c>, <c>0</c> to <c>9</c> and <c>-</c>.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <returns><see langword="true"/> when the name has exactly one slash and two parts that are not empty.</returns>
    public static bool IsValidName(ReadOnlySpan<char> name)
    {
        var slash = name.IndexOf('/');
        return slash > 0 && IsValidPart(name[..slash]) && IsValidPart(name[(slash + 1)..]);
    }

    /// <summary>
    /// Gets the namespace of a method or event name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The text before the first slash, or the whole name when it has no slash.</returns>
    public static ReadOnlySpan<char> NamespaceOf(ReadOnlySpan<char> name)
    {
        var slash = name.IndexOf('/');
        return slash < 0 ? name : name[..slash];
    }

    private static bool IsValidPart(ReadOnlySpan<char> part)
    {
        if (part.IsEmpty)
        {
            return false;
        }

        foreach (var c in part)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
