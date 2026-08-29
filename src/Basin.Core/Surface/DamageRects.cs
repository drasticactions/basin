using System.Runtime.CompilerServices;

namespace Basin;

public struct DamageRects
{
    public const int Capacity = 4;

    [InlineArray(Capacity)]
    private struct Slots
    {
        private Box _element0;
    }

    private Slots _slots;
    private int _count;
    private bool _overflowed;

    public readonly int Count => _count;

    public readonly bool Overflowed => _overflowed;

    public readonly Box this[int index] => index >= 0 && index < _count ? _slots[index] : default;

    public void Clear()
    {
        _count = 0;
        _overflowed = false;
    }

    public void Add(in Box box)
    {
        if (box.IsEmpty)
        {
            return;
        }

        if (_overflowed)
        {
            _slots[0] = Union(_slots[0], box);
            return;
        }

        if (_count == Capacity)
        {
            var hull = box;
            for (var i = 0; i < _count; i++)
            {
                hull = Union(hull, _slots[i]);
            }

            _slots[0] = hull;
            _count = 1;
            _overflowed = true;
            return;
        }

        _slots[_count++] = box;
    }

    public void Add(int x, int y, int width, int height) => Add(new Box(x, y, width, height));

    public void Add(in DamageRects other)
    {
        for (var i = 0; i < other._count; i++)
        {
            Add(other._slots[i]);
        }
    }

    private static Box Union(in Box a, in Box b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        return new Box(x, y, right - x, bottom - y);
    }
}
