using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_single_pixel_buffer_manager_v1</c> global. It creates one-pixel solid color buffers.
/// </summary>
public sealed class SinglePixelBufferManager : IDisposable, IWpSinglePixelBufferManagerV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly ClientBufferRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="SinglePixelBufferManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="registry">The registry that the new buffers go into.</param>
    public SinglePixelBufferManager(WlServerDisplay display, ClientBufferRegistry registry)
    {
        _registry = registry;
        _global = display.CreateGlobal(WpSinglePixelBufferManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Removes the global. Buffers that exist stay valid.
    /// </summary>
    public void Dispose() => _global.Dispose();

    void IWpSinglePixelBufferManagerV1Handler.Destroy(WpSinglePixelBufferManagerV1Resource resource)
    {
    }

    void IWpSinglePixelBufferManagerV1Handler.CreateU32RgbaBuffer(WpSinglePixelBufferManagerV1Resource resource, in WpSinglePixelBufferManagerV1Resource.CreateU32RgbaBufferEventArgs args)
    {
        var buffer = new MemoryBuffer(1, 1, DrmFormat.Argb8888);
        if (buffer.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            static byte To8(uint value) => (byte)(value >> 24);
            unsafe
            {
                *(uint*)view.Data = (uint)(To8(args.A) << 24 | To8(args.R) << 16 | To8(args.G) << 8 | To8(args.B));
            }

            buffer.EndDataAccess();
        }

        var bufferResource = new WlBufferResource(resource.Client, 1, args.Id);
        _registry.Register(bufferResource.RawHandle, buffer);
        buffer.Released += () =>
        {
            if (!bufferResource.IsDestroyed)
            {
                bufferResource.SendRelease();
            }
        };
        bufferResource.Destroyed += (_, _) => buffer.Destroy();
    }

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new WpSinglePixelBufferManagerV1Resource(client, version, id, this);
}
