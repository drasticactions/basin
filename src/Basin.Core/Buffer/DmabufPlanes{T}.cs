using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;

namespace Basin;

[InlineArray(4)]
public struct DmabufPlanes<T>
{
    private T _element;
}
