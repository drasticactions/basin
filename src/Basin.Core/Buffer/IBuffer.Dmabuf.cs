using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;

namespace Basin;

public partial interface IBuffer
{
    bool TryGetDmabuf(out DmabufAttributes attributes)
    {
        attributes = default;
        return false;
    }
}
