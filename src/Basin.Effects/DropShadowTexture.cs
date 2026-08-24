namespace Basin.Effects;

public sealed class DropShadowTexture : IDisposable
{
    private const double GaussianScale = 3.0 * 2.5066282746310002 / 4.0 * 1.5;

    private MemoryBuffer? _buffer;

    private DropShadowTexture(
        MemoryBuffer buffer,
        double scale,
        Box center,
        double left,
        double top,
        double right,
        double bottom)
    {
        _buffer = buffer;
        Scale = scale;
        Center = center;
        PaddingLeft = left;
        PaddingTop = top;
        PaddingRight = right;
        PaddingBottom = bottom;
    }

    public IBuffer Buffer => _buffer ?? throw new ObjectDisposedException(nameof(DropShadowTexture));

    public double Scale { get; }

    public int Width => _buffer?.Width ?? 0;

    public int Height => _buffer?.Height ?? 0;

    public Box Center { get; }

    public double PaddingLeft { get; }

    public double PaddingTop { get; }

    public double PaddingRight { get; }

    public double PaddingBottom { get; }

    public static DropShadowTexture? Build(in DropShadowOptions options, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(scale, 0);
        var strength = Math.Clamp(options.Strength, 0, 1) * Math.Clamp(options.Color.A, 0f, 1f);
        Span<Prepared> prepared = stackalloc Prepared[2];
        var count = 0;
        if (TryPrepare(options.Primary, strength, scale, out var primary))
        {
            prepared[count++] = primary;
        }

        if (TryPrepare(options.Secondary, strength, scale, out var secondary))
        {
            prepared[count++] = secondary;
        }

        if (count == 0)
        {
            return null;
        }

        var extent = 0;
        for (var i = 0; i < count; i++)
        {
            extent = Math.Max(extent, prepared[i].Extent);
        }

        var box = (2 * extent) + 1;
        var width = 0;
        var height = 0;
        for (var i = 0; i < count; i++)
        {
            var layer = prepared[i];
            width = Math.Max(width, box + (2 * layer.Extent) + Math.Abs(layer.OffsetX));
            height = Math.Max(height, box + (2 * layer.Extent) + Math.Abs(layer.OffsetY));
        }

        var canvas = new uint[width * height];
        var boxX = (width - box) / 2;
        var boxY = (height - box) / 2;
        var cornerRadius = (Math.Max(0, options.CornerRadius) * scale) + 0.5;

        for (var i = 0; i < count; i++)
        {
            var layer = prepared[i];
            var side = box + (2 * layer.Extent);
            var alpha = new byte[side * side];
            FillRoundedRect(alpha, side, side, layer.Extent, layer.Extent, box, box, cornerRadius);
            BlurAlpha(alpha, side, side, layer.Radius);
            Composite(
                canvas, width, height, alpha, side, side,
                boxX - layer.Extent + layer.OffsetX,
                boxY - layer.Extent + layer.OffsetY,
                options.Color, layer.Alpha);
        }

        var overlap = options.Overlap * scale;
        var offsetX = options.OffsetX * scale;
        var offsetY = options.OffsetY * scale;
        var padLeft = boxX - overlap - offsetX;
        var padTop = boxY - overlap - offsetY;
        var padRight = width - (boxX + box) - overlap + offsetX;
        var padBottom = height - (boxY + box) - overlap + offsetY;

        var inset = 2.0 * scale;
        PunchRoundedRect(
            canvas, width, height,
            padLeft + inset, padTop + inset,
            width - padLeft - padRight - (2 * inset),
            height - padTop - padBottom - (2 * inset),
            cornerRadius);

        var buffer = new MemoryBuffer(width, height, DrmFormat.Argb8888);
        if (buffer.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            try
            {
                unsafe
                {
                    for (var y = 0; y < height; y++)
                    {
                        var row = new Span<uint>((uint*)(view.Data + (y * view.Stride)), width);
                        canvas.AsSpan(y * width, width).CopyTo(row);
                    }
                }
            }
            finally
            {
                buffer.EndDataAccess();
            }
        }

        return new DropShadowTexture(
            buffer, scale, new Box(width / 2, height / 2, 1, 1),
            Math.Max(0, padLeft) / scale,
            Math.Max(0, padTop) / scale,
            Math.Max(0, padRight) / scale,
            Math.Max(0, padBottom) / scale);
    }

    public void Dispose()
    {
        _buffer?.Destroy();
        _buffer = null;
    }

    private static bool TryPrepare(in DropShadowLayer layer, double strength, double scale, out Prepared prepared)
    {
        var alpha = Math.Clamp(layer.Opacity, 0, 1) * strength;
        if (alpha <= 0)
        {
            prepared = default;
            return false;
        }

        var radius = Math.Max(0, layer.Radius) * scale;
        prepared = new Prepared(
            (int)Math.Round(layer.OffsetX * scale, MidpointRounding.AwayFromZero),
            (int)Math.Round(layer.OffsetY * scale, MidpointRounding.AwayFromZero),
            radius,
            alpha,
            BlurExtent(radius));
        return true;
    }

    private static int BlurRadius(double standardDeviation) =>
        Math.Max(2, (int)Math.Floor((standardDeviation * GaussianScale) + 0.5));

    private static int BlurExtent(double radius) => BlurRadius(radius * 0.5);

    private static void ComputeLobes(int blurRadius, Span<int> lobes)
    {
        var z = blurRadius / 3;
        int major;
        int minor;
        int final;
        switch (blurRadius % 3)
        {
            case 0:
                major = z;
                minor = z;
                final = z;
                break;
            case 1:
                major = z + 1;
                minor = z;
                final = z;
                break;
            default:
                major = z + 1;
                minor = z;
                final = z + 1;
                break;
        }

        lobes[0] = major;
        lobes[1] = minor;
        lobes[2] = minor;
        lobes[3] = major;
        lobes[4] = final;
        lobes[5] = final;
    }

    private static void BlurAlpha(Span<byte> plane, int width, int height, double radius)
    {
        var blurRadius = BlurRadius(radius * 0.5);
        if (blurRadius < 2)
        {
            return;
        }

        Span<int> lobes = stackalloc int[6];
        ComputeLobes(blurRadius, lobes);

        var span = Math.Max(width, height);
        var scratch = new byte[2 * span];
        var first = scratch.AsSpan(0, span);
        var second = scratch.AsSpan(span, span);

        for (var y = 0; y < height; y++)
        {
            var row = plane.Slice(y * width, width);
            BoxBlurRow(row, 1, first, 1, width, lobes[0], lobes[1]);
            BoxBlurRow(first, 1, second, 1, width, lobes[2], lobes[3]);
            BoxBlurRow(second, 1, row, 1, width, lobes[4], lobes[5]);
        }

        for (var x = 0; x < width; x++)
        {
            var column = plane[x..];
            BoxBlurRow(column, width, first, 1, height, lobes[0], lobes[1]);
            BoxBlurRow(first, 1, second, 1, height, lobes[2], lobes[3]);
            BoxBlurRow(second, 1, column, width, height, lobes[4], lobes[5]);
        }
    }

    private static void BoxBlurRow(
        ReadOnlySpan<byte> source,
        int sourceStep,
        Span<byte> destination,
        int destinationStep,
        int count,
        int left,
        int right)
    {
        var boxSize = left + 1 + right;
        var reciprocal = (uint)((1 << 24) / boxSize);
        var sum = (uint)((boxSize + 1) / 2);
        var first = source[0];
        var last = source[(count - 1) * sourceStep];
        sum += (uint)(first * left);

        var read = 0;
        var trail = 0;
        var write = 0;

        var fillEnd = Math.Min(boxSize - left, count);
        while (read < fillEnd)
        {
            sum += source[read * sourceStep];
            read++;
        }

        var leadEnd = Math.Min(boxSize, count);
        while (read < leadEnd)
        {
            destination[write * destinationStep] = (byte)((sum * reciprocal) >> 24);
            sum = unchecked(sum + (uint)(source[read * sourceStep] - first));
            read++;
            write++;
        }

        while (read < count)
        {
            destination[write * destinationStep] = (byte)((sum * reciprocal) >> 24);
            sum = unchecked(sum + (uint)(source[read * sourceStep] - source[trail * sourceStep]));
            trail++;
            read++;
            write++;
        }

        while (write < count)
        {
            destination[write * destinationStep] = (byte)((sum * reciprocal) >> 24);
            sum = unchecked(sum + (uint)(last - source[trail * sourceStep]));
            trail++;
            write++;
        }
    }

    private static void FillRoundedRect(
        Span<byte> alpha, int width, int height, double x, double y, double boxWidth, double boxHeight, double radius)
    {
        var centerX = x + (boxWidth / 2);
        var centerY = y + (boxHeight / 2);
        var halfWidth = boxWidth / 2;
        var halfHeight = boxHeight / 2;
        var corner = Math.Clamp(radius, 0, Math.Min(halfWidth, halfHeight));

        for (var py = 0; py < height; py++)
        {
            for (var px = 0; px < width; px++)
            {
                var distance = RoundedDistance(px + 0.5 - centerX, py + 0.5 - centerY, halfWidth, halfHeight, corner);
                var coverage = Math.Clamp(0.5 - distance, 0, 1);
                alpha[(py * width) + px] = (byte)((coverage * 255) + 0.5);
            }
        }
    }

    private static void PunchRoundedRect(
        Span<uint> canvas, int width, int height, double x, double y, double boxWidth, double boxHeight, double radius)
    {
        if (boxWidth <= 0 || boxHeight <= 0)
        {
            return;
        }

        var centerX = x + (boxWidth / 2);
        var centerY = y + (boxHeight / 2);
        var halfWidth = boxWidth / 2;
        var halfHeight = boxHeight / 2;
        var corner = Math.Clamp(radius, 0, Math.Min(halfWidth, halfHeight));

        var left = Math.Max(0, (int)Math.Floor(x));
        var top = Math.Max(0, (int)Math.Floor(y));
        var right = Math.Min(width, (int)Math.Ceiling(x + boxWidth));
        var bottom = Math.Min(height, (int)Math.Ceiling(y + boxHeight));

        for (var py = top; py < bottom; py++)
        {
            for (var px = left; px < right; px++)
            {
                var distance = RoundedDistance(px + 0.5 - centerX, py + 0.5 - centerY, halfWidth, halfHeight, corner);
                var coverage = Math.Clamp(0.5 - distance, 0, 1);
                if (coverage <= 0)
                {
                    continue;
                }

                var index = (py * width) + px;
                var keep = 1 - coverage;
                canvas[index] = Pack(
                    Channel(canvas[index], 24) * keep,
                    Channel(canvas[index], 16) * keep,
                    Channel(canvas[index], 8) * keep,
                    Channel(canvas[index], 0) * keep);
            }
        }
    }

    private static void Composite(
        Span<uint> canvas,
        int width,
        int height,
        ReadOnlySpan<byte> alpha,
        int planeWidth,
        int planeHeight,
        int originX,
        int originY,
        RenderColor color,
        double opacity)
    {
        var red = Math.Clamp(color.R, 0f, 1f);
        var green = Math.Clamp(color.G, 0f, 1f);
        var blue = Math.Clamp(color.B, 0f, 1f);

        for (var y = 0; y < planeHeight; y++)
        {
            var destinationY = originY + y;
            if (destinationY < 0 || destinationY >= height)
            {
                continue;
            }

            for (var x = 0; x < planeWidth; x++)
            {
                var destinationX = originX + x;
                if (destinationX < 0 || destinationX >= width)
                {
                    continue;
                }

                var source = alpha[(y * planeWidth) + x] / 255.0 * opacity;
                if (source <= 0)
                {
                    continue;
                }

                var index = (destinationY * width) + destinationX;
                var inverse = 1 - source;
                canvas[index] = Pack(
                    source + (Channel(canvas[index], 24) * inverse),
                    (red * source) + (Channel(canvas[index], 16) * inverse),
                    (green * source) + (Channel(canvas[index], 8) * inverse),
                    (blue * source) + (Channel(canvas[index], 0) * inverse));
            }
        }
    }

    private static double RoundedDistance(double dx, double dy, double halfWidth, double halfHeight, double radius)
    {
        var qx = Math.Abs(dx) - (halfWidth - radius);
        var qy = Math.Abs(dy) - (halfHeight - radius);
        var ax = Math.Max(qx, 0);
        var ay = Math.Max(qy, 0);
        return Math.Sqrt((ax * ax) + (ay * ay)) + Math.Min(Math.Max(qx, qy), 0) - radius;
    }

    private static double Channel(uint value, int shift) => ((value >> shift) & 0xFF) / 255.0;

    private static uint Pack(double a, double r, double g, double b)
    {
        static uint Quantize(double value) => (uint)Math.Clamp((value * 255) + 0.5, 0, 255);
        return (Quantize(a) << 24) | (Quantize(r) << 16) | (Quantize(g) << 8) | Quantize(b);
    }

    private readonly record struct Prepared(int OffsetX, int OffsetY, double Radius, double Alpha, int Extent);
}
