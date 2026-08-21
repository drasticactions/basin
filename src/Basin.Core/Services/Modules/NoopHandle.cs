using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

internal sealed class NoopHandle : IDisposable
{
    public void Dispose()
    {
    }
}
