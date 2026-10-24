using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;

namespace Basin;

public partial interface IBuffer
{
    /// <summary>
    /// Gets the dmabuf planes behind the buffer. The buffer keeps ownership of the file descriptors.
    /// </summary>
    /// <param name="attributes">The dmabuf description, or the default value when the call fails.</param>
    /// <returns><see langword="true"/> when the buffer is a live dmabuf. The default implementation returns <see langword="false"/>.</returns>
    bool TryGetDmabuf(out DmabufAttributes attributes)
    {
        attributes = default;
        return false;
    }
}
