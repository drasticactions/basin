using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Basin.Protocol;
using Pixman;
using Wayland;

namespace Basin.Backend.Wayland;

public readonly record struct HostFrameInsets(int Top, int Right, int Bottom, int Left)
{
    public bool IsEmpty => Top == 0 && Right == 0 && Bottom == 0 && Left == 0;
}
