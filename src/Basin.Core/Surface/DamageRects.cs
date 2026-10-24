using System.Runtime.CompilerServices;

namespace Basin;

/// <summary>
/// Up to <see cref="Capacity"/> damage rectangles without allocation. A fifth rectangle collapses all of them into one bounding box.
/// </summary>
public struct DamageRects
{
    /// <summary>The number of rectangles kept before they collapse into one bounding box.</summary>
    public const int Capacity = 4;

    [InlineArray(Capacity)]
    private struct Slots
    {
        private Box _element0;
    }

    private Slots _slots;
    private int _count;
    private bool _overflowed;

    /// <summary>Gets the number of rectangles.</summary>
    public readonly int Count => _count;

    /// <summary>Gets a value indicating whether the rectangles collapsed into one bounding box.</summary>
    public readonly bool Overflowed => _overflowed;

    /// <summary>Gets one rectangle.</summary>
    /// <param name="index">The zero-based index.</param>
    /// <returns>The rectangle, or an empty box when <paramref name="index"/> is out of range.</returns>
    public readonly Box this[int index] => index >= 0 && index < _count ? _slots[index] : default;

    /// <summary>Removes all rectangles and resets <see cref="Overflowed"/>.</summary>
    public void Clear()
    {
        _count = 0;
        _overflowed = false;
    }

    /// <summary>Adds a rectangle. An empty box is ignored.</summary>
    /// <param name="box">The rectangle to add.</param>
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

    /// <summary>Adds a rectangle from its position and size. An empty rectangle is ignored.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    public void Add(int x, int y, int width, int height) => Add(new Box(x, y, width, height));

    /// <summary>Adds each rectangle of another set.</summary>
    /// <param name="other">The set to add.</param>
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
