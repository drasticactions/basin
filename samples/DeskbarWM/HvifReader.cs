using SkiaSharp;

namespace DeskbarWm;

internal sealed class HvifReader
{
    private const uint Magic = 0x6669636e;

    private readonly byte[] _data;
    private int _at;

    private readonly List<Style> _styles = [];
    private readonly List<HvifPath> _paths = [];
    private readonly List<Shape> _shapes = [];

    private HvifReader(byte[] data) => _data = data;

    public static HvifReader? Parse(byte[] data)
    {
        var reader = new HvifReader(data);
        return reader.ParseSections() ? reader : null;
    }

    public void Render(SKCanvas canvas, int sizePx)
    {
        canvas.Save();
        var scale = sizePx / 64f;
        canvas.Scale(scale);

        using var paint = new SKPaint();
        paint.IsAntialias = true;

        foreach (var shape in _shapes)
        {
            if (shape.StyleIndex >= _styles.Count)
            {
                continue;
            }

            var builder = new SKPathBuilder();
            foreach (var pathIndex in shape.PathIndices)
            {
                if (pathIndex < _paths.Count)
                {
                    using var part = _paths[pathIndex].ToPath();
                    builder.AddPath(part);
                }
            }

            using var path = builder.Detach();
            canvas.Save();
            if (shape.Transform is { } matrix)
            {
                canvas.Concat(in matrix);
            }

            var style = _styles[shape.StyleIndex];
            paint.Shader = null;
            if (style.Gradient is { } gradient)
            {
                paint.Color = SKColors.White;
                paint.Shader = BuildGradient(gradient);
            }
            else
            {
                paint.Color = style.Color;
            }

            if (shape.StrokeWidth is { } strokeWidth)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = strokeWidth;
                paint.StrokeJoin = shape.StrokeJoin;
                paint.StrokeCap = shape.StrokeCap;
            }
            else
            {
                paint.Style = SKPaintStyle.Fill;
            }

            canvas.DrawPath(path, paint);
            paint.Shader = null;
            canvas.Restore();
        }

        canvas.Restore();
    }

    private static SKShader? BuildGradient(GradientStyle gradient)
    {
        var colors = gradient.Colors.ToArray();
        var offsets = gradient.Offsets.ToArray();
        if (colors.Length == 0)
        {
            return null;
        }

        var matrix = gradient.Transform ?? SKMatrix.Identity;
        return gradient.Kind switch
        {
            2 or 4 or 5 => SKShader.CreateRadialGradient(
                new SKPoint(0, 0), 64f, colors, offsets, SKShaderTileMode.Clamp, matrix),
            1 => SKShader.CreateRadialGradient(
                new SKPoint(0, 0), 64f, colors, offsets, SKShaderTileMode.Clamp, matrix),
            3 => SKShader.CreateSweepGradient(new SKPoint(0, 0), colors, offsets, matrix),
            _ => SKShader.CreateLinearGradient(
                new SKPoint(-64, 0), new SKPoint(64, 0), colors, offsets, SKShaderTileMode.Clamp, matrix),
        };
    }

    private bool ParseSections()
    {
        if (!ReadUint32(out var magic) || magic != Magic)
        {
            return false;
        }

        return ParseStyles() && ParsePaths() && ParseShapes();
    }

    private bool ParseStyles()
    {
        if (!ReadByte(out var count))
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (!ReadByte(out var type))
            {
                return false;
            }

            switch (type)
            {
                case 1:
                    if (!ReadColor(alpha: true, gray: false, out var solid))
                    {
                        return false;
                    }

                    _styles.Add(new Style(solid, null));
                    break;
                case 3:
                    if (!ReadColor(alpha: false, gray: false, out var opaque))
                    {
                        return false;
                    }

                    _styles.Add(new Style(opaque, null));
                    break;
                case 4:
                    if (!ReadColor(alpha: true, gray: true, out var grayAlpha))
                    {
                        return false;
                    }

                    _styles.Add(new Style(grayAlpha, null));
                    break;
                case 5:
                    if (!ReadColor(alpha: false, gray: true, out var gray))
                    {
                        return false;
                    }

                    _styles.Add(new Style(gray, null));
                    break;
                case 2:
                    if (ReadGradient() is not { } gradient)
                    {
                        return false;
                    }

                    _styles.Add(new Style(SKColors.White, gradient));
                    break;
                default:
                    if (!SkipTag())
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    private GradientStyle? ReadGradient()
    {
        if (!ReadByte(out var kind) || !ReadByte(out var flags) || !ReadByte(out var stops))
        {
            return null;
        }

        SKMatrix? transform = null;
        if ((flags & 0x02) != 0)
        {
            if (ReadTransform() is not { } matrix)
            {
                return null;
            }

            transform = matrix;
        }

        var alpha = (flags & 0x04) == 0;
        var gray = (flags & 0x10) != 0;
        var gradient = new GradientStyle(kind, transform);
        for (var i = 0; i < stops; i++)
        {
            if (!ReadByte(out var offset) || !ReadColor(alpha, gray, out var color))
            {
                return null;
            }

            gradient.Offsets.Add(offset / 255f);
            gradient.Colors.Add(color);
        }

        return gradient;
    }

    private bool ParsePaths()
    {
        if (!ReadByte(out var count))
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (!ReadByte(out var flags) || !ReadByte(out var pointCount))
            {
                return false;
            }

            var path = new HvifPath { Closed = (flags & 0x02) != 0 };
            bool ok;
            if ((flags & 0x08) != 0)
            {
                ok = ReadPathNoCurves(path, pointCount);
            }
            else if ((flags & 0x04) != 0)
            {
                ok = ReadPathWithCommands(path, pointCount);
            }
            else
            {
                ok = ReadPathCurves(path, pointCount);
            }

            if (!ok)
            {
                return false;
            }

            _paths.Add(path);
        }

        return true;
    }

    private bool ReadPathNoCurves(HvifPath path, int pointCount)
    {
        for (var p = 0; p < pointCount; p++)
        {
            if (!ReadCoord(out var x) || !ReadCoord(out var y))
            {
                return false;
            }

            var point = new SKPoint(x, y);
            path.Points.Add((point, point, point));
        }

        return true;
    }

    private bool ReadPathCurves(HvifPath path, int pointCount)
    {
        for (var p = 0; p < pointCount; p++)
        {
            if (!ReadCoord(out var x) || !ReadCoord(out var y)
                || !ReadCoord(out var inX) || !ReadCoord(out var inY)
                || !ReadCoord(out var outX) || !ReadCoord(out var outY))
            {
                return false;
            }

            path.Points.Add((new SKPoint(x, y), new SKPoint(inX, inY), new SKPoint(outX, outY)));
        }

        return true;
    }

    private bool ReadPathWithCommands(HvifPath path, int pointCount)
    {
        var commandBytes = (pointCount + 3) / 4;
        if (_at + commandBytes > _data.Length)
        {
            return false;
        }

        var commandsAt = _at;
        _at += commandBytes;

        var last = new SKPoint(0, 0);
        for (var p = 0; p < pointCount; p++)
        {
            var command = (_data[commandsAt + (p / 4)] >> ((p % 4) * 2)) & 0x03;
            SKPoint point;
            var pointIn = default(SKPoint);
            var pointOut = default(SKPoint);
            switch (command)
            {
                case 0:
                    if (!ReadCoord(out var hx))
                    {
                        return false;
                    }

                    point = new SKPoint(hx, last.Y);
                    pointIn = point;
                    pointOut = point;
                    break;
                case 1:
                    if (!ReadCoord(out var vy))
                    {
                        return false;
                    }

                    point = new SKPoint(last.X, vy);
                    pointIn = point;
                    pointOut = point;
                    break;
                case 2:
                    if (!ReadCoord(out var lx) || !ReadCoord(out var ly))
                    {
                        return false;
                    }

                    point = new SKPoint(lx, ly);
                    pointIn = point;
                    pointOut = point;
                    break;
                default:
                    if (!ReadCoord(out var cx) || !ReadCoord(out var cy)
                        || !ReadCoord(out var cinX) || !ReadCoord(out var cinY)
                        || !ReadCoord(out var coutX) || !ReadCoord(out var coutY))
                    {
                        return false;
                    }

                    point = new SKPoint(cx, cy);
                    pointIn = new SKPoint(cinX, cinY);
                    pointOut = new SKPoint(coutX, coutY);
                    break;
            }

            path.Points.Add((point, pointIn, pointOut));
            last = point;
        }

        return true;
    }

    private bool ParseShapes()
    {
        if (!ReadByte(out var count))
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (!ReadByte(out var type))
            {
                return false;
            }

            if (type != 10)
            {
                if (!SkipTag())
                {
                    return false;
                }

                continue;
            }

            if (!ReadPathSourceShape())
            {
                return false;
            }
        }

        return true;
    }

    private bool ReadPathSourceShape()
    {
        if (!ReadByte(out var styleIndex) || !ReadByte(out var pathCount))
        {
            return false;
        }

        var shape = new Shape(styleIndex);
        for (var i = 0; i < pathCount; i++)
        {
            if (!ReadByte(out var pathIndex))
            {
                return false;
            }

            shape.PathIndices.Add(pathIndex);
        }

        if (!ReadByte(out var flags))
        {
            return false;
        }

        if ((flags & 0x02) != 0)
        {
            if (ReadTransform() is not { } matrix)
            {
                return false;
            }

            shape.Transform = matrix;
        }
        else if ((flags & 0x20) != 0)
        {
            if (!ReadCoord(out var tx) || !ReadCoord(out var ty))
            {
                return false;
            }

            shape.Transform = SKMatrix.CreateTranslation(tx, ty);
        }

        if ((flags & 0x08) != 0)
        {
            if (!ReadByte(out _) || !ReadByte(out _))
            {
                return false;
            }
        }

        if ((flags & 0x10) != 0)
        {
            if (!ReadByte(out var transformerCount))
            {
                return false;
            }

            for (var i = 0; i < transformerCount; i++)
            {
                if (!ReadTransformer(shape))
                {
                    return false;
                }
            }
        }

        _shapes.Add(shape);
        return true;
    }

    private bool ReadTransformer(Shape shape)
    {
        if (!ReadByte(out var type))
        {
            return false;
        }

        switch (type)
        {
            case 20:
                for (var i = 0; i < 6; i++)
                {
                    if (!ReadFloat32(out _))
                    {
                        return false;
                    }
                }

                return true;
            case 21:
                return ReadByte(out _) && ReadByte(out _) && ReadByte(out _);
            case 22:
                for (var i = 0; i < 9; i++)
                {
                    if (!ReadFloat24(out _))
                    {
                        return false;
                    }
                }

                return true;
            case 23:
                if (!ReadByte(out var width) || !ReadByte(out var lineOptions) || !ReadByte(out _))
                {
                    return false;
                }

                shape.StrokeWidth = MathF.Max(width - 128f, 0.1f);
                shape.StrokeJoin = (lineOptions & 15) switch
                {
                    1 or 2 => SKStrokeJoin.Bevel,
                    3 or 4 or 5 => SKStrokeJoin.Round,
                    _ => SKStrokeJoin.Miter,
                };
                shape.StrokeCap = (lineOptions >> 4) switch
                {
                    1 => SKStrokeCap.Square,
                    2 => SKStrokeCap.Round,
                    _ => SKStrokeCap.Butt,
                };
                return true;
            default:
                return SkipTag();
        }
    }

    private SKMatrix? ReadTransform()
    {
        Span<float> m = stackalloc float[6];
        for (var i = 0; i < 6; i++)
        {
            if (!ReadFloat24(out m[i]))
            {
                return null;
            }
        }

        return new SKMatrix(m[0], m[2], m[4], m[1], m[3], m[5], 0, 0, 1);
    }

    private bool ReadColor(bool alpha, bool gray, out SKColor color)
    {
        color = default;
        byte r;
        byte g;
        byte b;
        byte a = 255;
        if (gray)
        {
            if (!ReadByte(out r))
            {
                return false;
            }

            g = r;
            b = r;
            if (alpha && !ReadByte(out a))
            {
                return false;
            }
        }
        else
        {
            if (!ReadByte(out r) || !ReadByte(out g) || !ReadByte(out b))
            {
                return false;
            }

            if (alpha && !ReadByte(out a))
            {
                return false;
            }
        }

        color = new SKColor(r, g, b, a);
        return true;
    }

    private bool ReadCoord(out float coord)
    {
        coord = 0;
        if (!ReadByte(out var value))
        {
            return false;
        }

        if ((value & 128) != 0)
        {
            if (!ReadByte(out var low))
            {
                return false;
            }

            var packed = ((value & 127) << 8) | low;
            coord = (packed / 102f) - 128f;
            return true;
        }

        coord = value - 32f;
        return true;
    }

    private bool ReadFloat24(out float value)
    {
        value = 0;
        if (!ReadByte(out var b0) || !ReadByte(out var b1) || !ReadByte(out var b2))
        {
            return false;
        }

        var packed = (b0 << 16) | (b1 << 8) | b2;
        if (packed == 0)
        {
            return true;
        }

        var sign = (packed & 0x800000) >> 23;
        var exponent = ((packed & 0x7e0000) >> 17) - 32;
        var mantissa = (packed & 0x01ffff) << 6;
        var bits = (uint)((sign << 31) | ((exponent + 127) << 23) | mantissa);
        value = BitConverter.UInt32BitsToSingle(bits);
        return true;
    }

    private bool ReadFloat32(out float value)
    {
        value = 0;
        if (_at + 4 > _data.Length)
        {
            return false;
        }

        value = BitConverter.ToSingle(_data, _at);
        _at += 4;
        return true;
    }

    private bool ReadUint32(out uint value)
    {
        value = 0;
        if (_at + 4 > _data.Length)
        {
            return false;
        }

        value = BitConverter.ToUInt32(_data, _at);
        _at += 4;
        return true;
    }

    private bool ReadByte(out byte value)
    {
        if (_at >= _data.Length)
        {
            value = 0;
            return false;
        }

        value = _data[_at++];
        return true;
    }

    private bool SkipTag()
    {
        if (_at + 2 > _data.Length)
        {
            return false;
        }

        var length = BitConverter.ToUInt16(_data, _at);
        _at += 2 + length;
        return _at <= _data.Length;
    }

    private sealed record Style(SKColor Color, GradientStyle? Gradient);

    private sealed class GradientStyle(byte kind, SKMatrix? transform)
    {
        public byte Kind { get; } = kind;

        public SKMatrix? Transform { get; } = transform;

        public List<SKColor> Colors { get; } = [];

        public List<float> Offsets { get; } = [];
    }

    private sealed class Shape(byte styleIndex)
    {
        public byte StyleIndex { get; } = styleIndex;

        public List<int> PathIndices { get; } = [];

        public SKMatrix? Transform { get; set; }

        public float? StrokeWidth { get; set; }

        public SKStrokeJoin StrokeJoin { get; set; } = SKStrokeJoin.Miter;

        public SKStrokeCap StrokeCap { get; set; } = SKStrokeCap.Butt;
    }
}
