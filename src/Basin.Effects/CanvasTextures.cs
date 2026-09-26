namespace Basin.Effects;

public static class CanvasTextures
{
    public const int Size = 256;

    public static ReadOnlySpan<string> Names => NameList;

    private static readonly string[] NameList = ["stone", "brick", "wood", "noise"];

    public static bool TryParse(string name, out CanvasTexturePreset preset)
    {
        for (var i = 0; i < NameList.Length; i++)
        {
            if (string.Equals(NameList[i], name, StringComparison.Ordinal))
            {
                preset = (CanvasTexturePreset)i;
                return true;
            }
        }

        preset = default;
        return false;
    }

    public static string NameOf(CanvasTexturePreset preset) => NameList[(int)preset];

    public static MemoryBuffer Generate(CanvasTexturePreset preset)
    {
        var luminance = new float[Size * Size];
        switch (preset)
        {
            case CanvasTexturePreset.Stone:
                Stone(luminance);
                break;
            case CanvasTexturePreset.Brick:
                Brick(luminance);
                break;
            case CanvasTexturePreset.Wood:
                Wood(luminance);
                break;
            default:
                NoiseField(luminance);
                break;
        }

        var buffer = new MemoryBuffer(Size, Size, DrmFormat.Argb8888);
        if (buffer.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            try
            {
                unsafe
                {
                    for (var y = 0; y < Size; y++)
                    {
                        var row = (uint*)(view.Data + (y * view.Stride));
                        for (var x = 0; x < Size; x++)
                        {
                            var value = (uint)Math.Clamp((int)((luminance[(y * Size) + x] * 255f) + 0.5f), 0, 255);
                            row[x] = 0xff000000u | (value << 16) | (value << 8) | value;
                        }
                    }
                }
            }
            finally
            {
                buffer.EndDataAccess();
            }
        }

        return buffer;
    }

    private static void Stone(Span<float> into)
    {
        const int courses = 4;
        const int height = Size / courses;
        const int mortar = 3;
        Span<int> joints = stackalloc int[16];
        for (var course = 0; course < courses; course++)
        {
            var count = 0;
            var shift = (int)(Hash(course, 0, 11) * Size);
            var at = 0;
            while (at < Size)
            {
                joints[count++] = (at + shift) % Size;
                var remaining = Size - at;
                var width = 48 + (int)(Hash(course, count, 12) * 64);
                at += remaining - width < 48 ? remaining : width;
            }

            var top = course * height;
            for (var y = top; y < top + height; y++)
            {
                var local = y - top;
                for (var x = 0; x < Size; x++)
                {
                    var block = BlockOf(joints[..count], x, out var fromLeft, out var toRight);
                    float value;
                    if (local < mortar || fromLeft < mortar)
                    {
                        value = 0.35f;
                    }
                    else
                    {
                        var seed = (course * 16) + block;
                        value = 0.75f + (0.12f * (float)((2.0 * ((0.65 * Value(x, y, 32, 20 + seed)) + (0.35 * Value(x, y, 16, 40 + seed)))) - 1.0));
                        value += (float)((Hash(course, block, 13) - 0.5) * 0.08);
                        if (local == mortar || fromLeft == mortar)
                        {
                            value += 0.1f;
                        }
                        else if (local == height - 1 || toRight == 1)
                        {
                            value -= 0.1f;
                        }
                    }

                    into[(y * Size) + x] = value;
                }
            }
        }
    }

    private static int BlockOf(ReadOnlySpan<int> joints, int x, out int fromLeft, out int toRight)
    {
        var best = 0;
        fromLeft = int.MaxValue;
        for (var i = 0; i < joints.Length; i++)
        {
            var distance = ((x - joints[i]) % Size + Size) % Size;
            if (distance < fromLeft)
            {
                fromLeft = distance;
                best = i;
            }
        }

        var next = joints[(best + 1) % joints.Length];
        var width = ((next - joints[best]) % Size + Size) % Size;
        width = width == 0 ? Size : width;
        toRight = width - fromLeft;
        return best;
    }

    private static void Brick(Span<float> into)
    {
        const int width = 64;
        const int height = 32;
        const int mortar = 2;
        for (var y = 0; y < Size; y++)
        {
            var course = y / height;
            var local = y % height;
            var shift = (course & 1) == 0 ? 0 : width / 2;
            for (var x = 0; x < Size; x++)
            {
                var along = (x + shift) % Size;
                var brick = along / width;
                float value;
                if (local < mortar || along % width < mortar)
                {
                    value = 0.4f;
                }
                else
                {
                    value = 0.62f + (float)((Hash(course, brick, 21) - 0.5) * 0.12);
                    value += (float)((Value(x, y, 8, 22) - 0.5) * 0.1);
                }

                into[(y * Size) + x] = value;
            }
        }
    }

    private static void Wood(Span<float> into)
    {
        const int planks = 4;
        const int height = Size / planks;
        const int joint = 2;
        for (var plank = 0; plank < planks; plank++)
        {
            var seed = 32 + (plank * 8);
            var tone = (float)((Hash(plank, 1, 33) - 0.5) * 0.12);
            var spacing = 2.6 + (Hash(plank, 2, 34) * 1.6);
            var pith = height * (0.55 + (0.5 * Hash(plank, 3, 35))) * (Hash(plank, 10, 42) < 0.5 ? -1.0 : 1.0);
            var phase = Hash(plank, 5, 37);
            var phase2 = Hash(plank, 6, 38);
            var end = (int)(Hash(plank, 0, 31) * Size);
            var knotted = Hash(plank, 4, 36) < 0.6;
            var knotX = Hash(plank, 7, 39) * Size;
            var knotY = (height * 0.25) + (Hash(plank, 8, 40) * height * 0.5);
            var knotRadius = 4.0 + (Hash(plank, 9, 41) * 4.0);
            for (var local = 0; local < height; local++)
            {
                var y = (plank * height) + local;
                for (var x = 0; x < Size; x++)
                {
                    var fromEnd = ((x - end) % Size + Size) % Size;
                    if (local < joint || fromEnd < joint)
                    {
                        into[(y * Size) + x] = 0.28f;
                        continue;
                    }

                    var depth = 30.0 + (22.0 * Math.Sin(2.0 * Math.PI * ((x / (double)Size) + phase))) +
                        (8.0 * Math.Sin(2.0 * Math.PI * ((2.0 * x / Size) + phase2)));
                    var across = local - (height / 2.0) - pith;
                    var radius = Math.Sqrt((across * across) + (depth * depth));
                    radius += (2.5 * (Value(x, y, 64, 16, seed) - 0.5)) + (0.8 * (Value(x, y, 16, 4, seed + 3) - 0.5));
                    var knot = 0.0;
                    if (knotted)
                    {
                        var dx = x - knotX;
                        dx -= Size * Math.Round(dx / Size);
                        var dy = local - knotY;
                        var distance = Math.Sqrt((dx * dx * 0.25) + (dy * dy));
                        var pull = 0.8 * Math.Exp(-(distance * distance) / (2.0 * 4.0 * knotRadius * knotRadius));
                        radius = (radius * (1.0 - pull)) + ((radius + (distance * 1.2)) * pull);
                        knot = distance < knotRadius ? 1.0 - (distance / knotRadius) : 0.0;
                    }

                    var ring = radius / spacing;
                    var f = ring - Math.Floor(ring);
                    var late = SmoothStep(0.6, 0.9, f) * (1.0 - SmoothStep(0.9, 1.0, f));
                    var fibers = (0.7 * (Value(x, y, 32, 2, seed + 1) - 0.5)) + (0.3 * (Value(x, y, 8, 1, seed + 2) - 0.5));
                    var value = 0.66 - (0.05 * f) - (0.13 * late) + (0.14 * fibers) + tone - (0.35 * Math.Sqrt(knot));
                    if (local == joint)
                    {
                        value += 0.05;
                    }
                    else if (local == height - 1)
                    {
                        value -= 0.05;
                    }

                    into[(y * Size) + x] = (float)value;
                }
            }
        }
    }

    private static double SmoothStep(double from, double to, double t)
    {
        var x = Math.Clamp((t - from) / (to - from), 0.0, 1.0);
        return x * x * (3.0 - (2.0 * x));
    }

    private static void NoiseField(Span<float> into)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var sum = (0.5 * Value(x, y, 64, 51)) + (0.25 * Value(x, y, 32, 52)) +
                    (0.125 * Value(x, y, 16, 53)) + (0.0625 * Value(x, y, 8, 54));
                into[(y * Size) + x] = (float)(0.5 + (0.45 * (sum / 0.9375)));
            }
        }
    }

    private static double Value(int x, int y, int period, int seed) => Value(x, y, period, period, seed);

    private static double Value(int x, int y, int periodX, int periodY, int seed)
    {
        var cellsX = Size / periodX;
        var cellsY = Size / periodY;
        var fx = (x + 0.5) / periodX;
        var fy = (y + 0.5) / periodY;
        var ix = (int)Math.Floor(fx);
        var iy = (int)Math.Floor(fy);
        var tx = Smooth(fx - ix);
        var ty = Smooth(fy - iy);
        var x0 = Wrap(ix, cellsX);
        var x1 = Wrap(ix + 1, cellsX);
        var y0 = Wrap(iy, cellsY);
        var y1 = Wrap(iy + 1, cellsY);
        var top = Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), tx);
        var bottom = Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), tx);
        return Lerp(top, bottom, ty);
    }

    private static int Wrap(int value, int cells) => ((value % cells) + cells) % cells;

    private static double Smooth(double t) => t * t * (3.0 - (2.0 * t));

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = ((uint)x * 374761393u) + ((uint)y * 668265263u) + ((uint)seed * 2246822519u);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / 4294967296.0;
        }
    }
}
