using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Basin.Protocol;
using Pixman;
using Wayland;

namespace Basin.Backend.Wayland;

/// <summary>
/// Holds the width of the host frame on each side of a nested output, in logical pixels.
/// </summary>
/// <param name="Top">The height of the band above the output.</param>
/// <param name="Right">The width of the band to the right of the output.</param>
/// <param name="Bottom">The height of the band below the output.</param>
/// <param name="Left">The width of the band to the left of the output.</param>
public readonly record struct HostFrameInsets(int Top, int Right, int Bottom, int Left)
{
    /// <summary>Gets a value indicating whether all four sides are zero.</summary>
    public bool IsEmpty => Top == 0 && Right == 0 && Bottom == 0 && Left == 0;
}
