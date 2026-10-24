using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;

namespace Basin;

/// <summary>
/// A fixed array of four values, one per dmabuf plane.
/// </summary>
/// <typeparam name="T">The value type of each plane entry.</typeparam>
[InlineArray(4)]
public struct DmabufPlanes<T>
{
    private T _element;
}
