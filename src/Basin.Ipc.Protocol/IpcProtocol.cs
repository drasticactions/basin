using System.Buffers.Binary;

namespace Basin.Ipc;

public static class IpcProtocol
{
    public const int Version = 1;

    public const int HeaderBytes = 4;

    public const int MaxRequestBytes = 1024 * 1024;

    public const int MaxMessageBytes = 32 * 1024 * 1024;

    public const string SocketVariable = "BASIN_SOCKET";

    public const string SocketPrefix = "basin-";

    public const string SocketSuffix = ".sock";

    public static uint ReadLength(ReadOnlySpan<byte> header) => BinaryPrimitives.ReadUInt32LittleEndian(header);

    public static void WriteLength(Span<byte> header, int length) =>
        BinaryPrimitives.WriteUInt32LittleEndian(header, checked((uint)length));

    public static string? SocketPath(string? runtimeDirectory, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (string.IsNullOrEmpty(runtimeDirectory))
        {
            return null;
        }

        return Path.Combine(runtimeDirectory, SocketPrefix + name + SocketSuffix);
    }

    public static bool IsValidName(ReadOnlySpan<char> name)
    {
        var slash = name.IndexOf('/');
        return slash > 0 && IsValidPart(name[..slash]) && IsValidPart(name[(slash + 1)..]);
    }

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
