using System.Runtime.CompilerServices;
using Pixman;
using Pixman.Native;

namespace Basin;

/// <summary>
/// Enumerates the rectangles of a pixman region with no allocation.
/// </summary>
public static class RegionRects
{
    /// <summary>
    /// Starts an enumeration of the rectangles of a region.
    /// </summary>
    /// <param name="region">The region to read. It must not change while the enumeration runs.</param>
    /// <returns>An enumerator that a <c>foreach</c> statement can use.</returns>
    public static Walker Of(PixmanRegion32 region) => new(region);

    /// <summary>
    /// A <c>foreach</c> enumerator over the rectangles of one region. It reads the storage of the region directly and becomes invalid when the region changes.
    /// </summary>
    public unsafe struct Walker
    {
        private readonly PixmanBox32 _single;
        private readonly PixmanBox32* _many;
        private readonly int _count;
        private int _index;

        internal Walker(PixmanRegion32 region)
        {
            var count = region.RectangleCount;
            if (count == 1)
            {
                _single = region.Extents;
                _many = null;
                _count = 1;
            }
            else if (count == 0)
            {
                _single = default;
                _many = null;
                _count = 0;
            }
            else
            {
                _single = default;
                fixed (pixman_region32* raw = &RegionOf(region))
                {
                    int fetched;
                    _many = (PixmanBox32*)Libpixman.pixman_region32_rectangles(raw, &fetched);
                    _count = fetched;
                }
            }

            _index = -1;
        }

        /// <summary>
        /// Gets the rectangle at the current position.
        /// </summary>
        public readonly PixmanBox32 Current => _many is null ? _single : _many[_index];

        /// <summary>
        /// Moves to the next rectangle.
        /// </summary>
        /// <returns><see langword="false"/> when no rectangles remain.</returns>
        public bool MoveNext() => ++_index < _count;

        /// <summary>
        /// Gets this enumerator, so that a <c>foreach</c> statement can use it directly.
        /// </summary>
        /// <returns>A copy of this enumerator.</returns>
        public readonly Walker GetEnumerator() => this;

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "Region")]
        private static extern ref pixman_region32 RegionOf(PixmanRegion32 region);
    }
}
