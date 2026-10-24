using Pixman;

namespace Basin;

/// <summary>
/// Tracks damage per frame for a swapchain of up to four buffers, so a renderer can repaint only what changed since a buffer was last drawn. Coordinates are buffer pixels, clipped to the ring's size.
/// </summary>
public sealed class DamageRing : IDisposable
{
    private const int Frames = 4;

    private readonly PixmanRegion32 _current = new();
    private readonly PixmanRegion32[] _previous;
    private int _head;
    private int _stored;

    /// <summary>
    /// Initializes a new instance of the <see cref="DamageRing"/> class. The current damage starts as the whole buffer.
    /// </summary>
    /// <param name="width">The buffer width in pixels.</param>
    /// <param name="height">The buffer height in pixels.</param>
    public DamageRing(int width, int height)
    {
        Width = width;
        Height = height;
        _previous = new PixmanRegion32[Frames];
        for (var i = 0; i < Frames; i++)
        {
            _previous[i] = new PixmanRegion32();
        }

        AddWhole();
    }

    /// <summary>
    /// Gets the buffer width in pixels that clips all damage. <see cref="Resize"/> changes it.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Gets the buffer height in pixels that clips all damage. <see cref="Resize"/> changes it.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the current frame has no damage since the last <see cref="Commit"/>.
    /// </summary>
    public bool IsEmpty => _current.IsEmpty;

    /// <summary>
    /// Adds a box to the current frame's damage. An empty box adds nothing. The box is clipped to the buffer.
    /// </summary>
    /// <param name="box">The damaged box in buffer pixels.</param>
    public void Add(in Box box)
    {
        if (!box.IsEmpty)
        {
            _current.UnionRect(_current, box.X, box.Y, (uint)box.Width, (uint)box.Height);
            _current.IntersectRect(_current, 0, 0, (uint)Width, (uint)Height);
        }
    }

    /// <summary>
    /// Adds a region to the current frame's damage, clipped to the buffer. The ring does not keep a reference to the region.
    /// </summary>
    /// <param name="region">The damaged region in buffer pixels.</param>
    public void Add(PixmanRegion32 region)
    {
        _current.UnionWith(region);
        _current.IntersectRect(_current, 0, 0, (uint)Width, (uint)Height);
    }

    /// <summary>
    /// Sets the current frame's damage to the whole buffer. A ring with a zero or negative size gets empty damage.
    /// </summary>
    public void AddWhole()
    {
        if (Width <= 0 || Height <= 0)
        {
            _current.Clear();
            return;
        }

        _current.Reset(new PixmanBox32(0, 0, Width, Height));
    }

    /// <summary>
    /// Writes the region that a buffer of the given age must repaint: the current damage plus the damage of the <paramref name="age"/> minus one frames before it.
    /// </summary>
    /// <param name="age">The buffer age as the swapchain reports it. Zero, a negative value or an age older than the ring's history gives the whole buffer.</param>
    /// <param name="result">The region that receives the damage. Its previous content is replaced.</param>
    public void GetBufferDamage(int age, PixmanRegion32 result)
    {
        if (age <= 0 || age > _stored + 1)
        {
            if (Width <= 0 || Height <= 0)
            {
                result.Clear();
                return;
            }

            result.Reset(new PixmanBox32(0, 0, Width, Height));
            return;
        }

        result.Copy(_current);
        for (var i = 1; i < age; i++)
        {
            result.UnionWith(_previous[(_head - i + Frames) % Frames]);
        }

        result.IntersectRect(result, 0, 0, (uint)Width, (uint)Height);
    }

    /// <summary>
    /// Ends the current frame. The current damage moves into the history, and the current damage becomes empty.
    /// </summary>
    public void Commit()
    {
        _previous[_head].Copy(_current);
        _head = (_head + 1) % Frames;
        _stored = Math.Min(_stored + 1, Frames);
        _current.Clear();
    }

    /// <summary>
    /// Changes the buffer size. A new size clears the history and damages the whole buffer. The same size does nothing.
    /// </summary>
    /// <param name="width">The new buffer width in pixels.</param>
    /// <param name="height">The new buffer height in pixels.</param>
    public void Resize(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        _stored = 0;
        AddWhole();
    }

    /// <summary>
    /// Releases the pixman regions that the ring holds. The ring must not be used after this call.
    /// </summary>
    public void Dispose()
    {
        _current.Dispose();
        foreach (var region in _previous)
        {
            region.Dispose();
        }
    }
}
