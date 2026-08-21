using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Basin.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin;

internal static class DmabufTestNames
{
    public static string Fourcc(uint fourcc) =>
        $"{(char)(fourcc & 0xFF)}{(char)((fourcc >> 8) & 0xFF)}{(char)((fourcc >> 16) & 0xFF)}{(char)((fourcc >> 24) & 0xFF)}";
}
