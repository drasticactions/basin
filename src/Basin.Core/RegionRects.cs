using System.Runtime.CompilerServices;
using Pixman;
using Pixman.Native;

namespace Basin;

public static class RegionRects
{
    public static Walker Of(PixmanRegion32 region) => new(region);

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

        public readonly PixmanBox32 Current => _many is null ? _single : _many[_index];

        public bool MoveNext() => ++_index < _count;

        public readonly Walker GetEnumerator() => this;

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "Region")]
        private static extern ref pixman_region32 RegionOf(PixmanRegion32 region);
    }
}
