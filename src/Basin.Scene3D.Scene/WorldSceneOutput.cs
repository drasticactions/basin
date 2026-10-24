using Basin.Scene;

namespace Basin.Scene3D.Scene;

/// <summary>
/// One output's view of a world, shown in a 2D scene. It keeps a ring of up to four buffers and shows the newest one in an opaque buffer node.
/// </summary>
public sealed class WorldSceneOutput : IDisposable
{
    private const int MaxBuffers = 4;

    private readonly List<BufferBase> _ring = [];
    private readonly IAllocator _allocator;
    private int _width;
    private int _height;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldSceneOutput"/> class. It puts its buffer node at the bottom of <paramref name="layer"/>.
    /// </summary>
    /// <param name="layer">The scene tree that holds the world's node.</param>
    /// <param name="allocator">The allocator for the ring's buffers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layer"/> or <paramref name="allocator"/> is <see langword="null"/>.</exception>
    public WorldSceneOutput(SceneTree layer, IAllocator allocator)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(allocator);
        _allocator = allocator;
        Node = new SceneBuffer(layer) { IsOpaque = true, InputEnabled = false, ExcludedFromCapture = false };
        Node.LowerToBottom();
    }

    /// <summary>
    /// Gets the opaque buffer node that shows the world. It takes no input.
    /// </summary>
    public SceneBuffer Node { get; }

    /// <summary>
    /// Gets what the last <see cref="Render"/> call saw. The consumer sends frame callbacks to its surfaces.
    /// </summary>
    public WorldVisibleSet Visible { get; } = new();

    /// <summary>
    /// Gets the number of <see cref="Render"/> calls that drew a frame.
    /// </summary>
    public long Frames { get; private set; }

    /// <summary>
    /// Walks the visible rooms, draws the world into a free buffer of the ring and shows it at <paramref name="slice"/>. A change of buffer size drops the ring.
    /// </summary>
    /// <param name="renderer">The 3D pass that draws the world.</param>
    /// <param name="world">The world to draw.</param>
    /// <param name="camera">The camera that spans the layout.</param>
    /// <param name="union">The union of the output layout, in layout pixels.</param>
    /// <param name="slice">The output's box in the layout. The node is placed and sized to it.</param>
    /// <param name="tick">The frame time.</param>
    /// <param name="settings">The limits for the walk and the pass.</param>
    /// <param name="bufferWidth">The buffer width in physical pixels.</param>
    /// <param name="bufferHeight">The buffer height in physical pixels.</param>
    /// <param name="modifiers">The output's scanout modifiers. With none, or when they fail, the ring uses a linear buffer.</param>
    /// <returns><see langword="false"/> when no buffer is free, the allocation fails, or the pass did not draw.</returns>
    public bool Render(
        IWorldRenderer renderer,
        World world,
        WorldCamera camera,
        in Box union,
        in Box slice,
        FrameTick tick,
        WorldRenderSettings settings,
        int bufferWidth,
        int bufferHeight,
        ReadOnlySpan<ulong> modifiers)
    {
        var width = Math.Max(1, bufferWidth);
        var height = Math.Max(1, bufferHeight);
        if (width != _width || height != _height)
        {
            DropRing();
            (_width, _height) = (width, height);
        }

        if (Take(modifiers) is not { } target)
        {
            return false;
        }

        var worldView = camera.ViewOf(union, slice);
        Visible.Clear();
        WorldVisibility.Collect(world, worldView, settings.RoomDepth, Visible);
        if (!renderer.Render(new WorldFrame(world, worldView, tick, Visible, settings), target))
        {
            return false;
        }

        Node.SetPosition(slice.X, slice.Y);
        Node.SetBuffer(target);
        Node.DestinationWidth = slice.Width;
        Node.DestinationHeight = slice.Height;
        Node.NotifyContentChanged();
        Frames++;
        return true;
    }

    /// <summary>
    /// Destroys the node and every buffer of the ring.
    /// </summary>
    public void Dispose()
    {
        Node.Destroy();
        foreach (var buffer in _ring)
        {
            buffer.Destroy();
        }

        _ring.Clear();
    }

    private BufferBase? Take(ReadOnlySpan<ulong> modifiers)
    {
        foreach (var buffer in _ring)
        {
            if (buffer.LockCount == 0 && !ReferenceEquals(buffer, Node.Buffer))
            {
                return buffer;
            }
        }

        if (_ring.Count >= MaxBuffers)
        {
            return null;
        }

        ReadOnlySpan<ulong> linear = [DrmFormatSet.ModifierLinear];
        var allocated = _allocator.Allocate(_width, _height, DrmFormat.Xrgb8888, modifiers.Length > 0 ? modifiers : linear, BufferUse.Render | BufferUse.Scanout)
            ?? _allocator.Allocate(_width, _height, DrmFormat.Xrgb8888, linear, BufferUse.Render);
        if (allocated is null)
        {
            return null;
        }

        _ring.Add(allocated);
        return allocated;
    }

    private void DropRing()
    {
        foreach (var buffer in _ring)
        {
            if (!ReferenceEquals(buffer, Node.Buffer))
            {
                buffer.Destroy();
            }
        }

        _ring.RemoveAll(buffer => !ReferenceEquals(buffer, Node.Buffer));
    }
}
