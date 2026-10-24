using Basin;
using Basin.Host;

namespace Inlet;

internal sealed class RenderStackLifetime(RenderStack first) : IDisposable
{
    public OutputDriver? Driver { get; set; }

    public void Dispose()
    {
        var allocator = Driver?.DeviceAllocator ?? first.DeviceAllocator;
        var renderer = Driver?.Renderer ?? first.Renderer;
        allocator?.Dispose();
        renderer.Dispose();
    }
}
