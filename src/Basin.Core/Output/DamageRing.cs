using Pixman;

namespace Basin;

public sealed class DamageRing : IDisposable
{
    private const int Frames = 4;

    private readonly PixmanRegion32 _current = new();
    private readonly PixmanRegion32[] _previous;
    private int _head;
    private int _stored;

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

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsEmpty => _current.IsEmpty;

    public void Add(in Box box)
    {
        if (!box.IsEmpty)
        {
            _current.UnionRect(_current, box.X, box.Y, (uint)box.Width, (uint)box.Height);
            _current.IntersectRect(_current, 0, 0, (uint)Width, (uint)Height);
        }
    }

    public void Add(PixmanRegion32 region)
    {
        _current.UnionWith(region);
        _current.IntersectRect(_current, 0, 0, (uint)Width, (uint)Height);
    }

    public void AddWhole()
    {
        if (Width <= 0 || Height <= 0)
        {
            _current.Clear();
            return;
        }

        _current.Reset(new PixmanBox32(0, 0, Width, Height));
    }

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

    public void Commit()
    {
        _previous[_head].Copy(_current);
        _head = (_head + 1) % Frames;
        _stored = Math.Min(_stored + 1, Frames);
        _current.Clear();
    }

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

    public void Dispose()
    {
        _current.Dispose();
        foreach (var region in _previous)
        {
            region.Dispose();
        }
    }
}
